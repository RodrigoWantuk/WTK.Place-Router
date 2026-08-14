using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using PlaceRouter.DesignExchange.Prdx;
using PlaceRouter.Domain.Prdx;

namespace PlaceRouter.DesignExchange.Tests;

internal static class PrdxTestData
{
    public static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PlaceRouter.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
        }
    }

    public static string FixtureProjectJsonPath =>
        Path.Combine(RepositoryRoot, "schemas", "prdx", "0.1", "examples", "minimal-2layer.project.json");

    public static CanonicalProject LoadFixtureProject() => CanonicalProject.Parse(File.ReadAllText(FixtureProjectJsonPath));

    public static string CreateFixturePrdx(string directory)
    {
        var project = LoadFixtureProject();
        var path = Path.Combine(directory, "minimal-2layer.prdx");
        var writer = new PrdxProjectWriter();
        var save = writer.Save(project, path);
        Assert.True(save.Success, string.Join(Environment.NewLine, save.Diagnostics.Select(d => d.Message)));
        return path;
    }

    public static string CreatePrdxWithWrongHash(string directory)
    {
        var projectBytes = Encoding.UTF8.GetBytes(File.ReadAllText(FixtureProjectJsonPath));
        var path = Path.Combine(directory, "wrong-hash.prdx");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        var manifest = new PrdxManifest(
            PrdxManifest.ExpectedFormat,
            PrdxManifest.CurrentFormatVersion,
            "prj_demo_001",
            1,
            "2026-08-14T19:00:00Z",
            "2026-08-14T19:00:00Z",
            "test",
            PrdxManifest.ProjectPayloadPath,
            new string('0', 64));

        WriteEntry(archive, PrdxManifest.ManifestPath, manifest.ToJsonObject().ToJsonString(new() { WriteIndented = true }));
        WriteEntry(archive, PrdxManifest.ProjectPayloadPath, Encoding.UTF8.GetString(projectBytes));
        return path;
    }

    public static string CreatePrdxWithBadManifestFormat(string directory)
    {
        var projectBytes = Encoding.UTF8.GetBytes(File.ReadAllText(FixtureProjectJsonPath));
        var path = Path.Combine(directory, "bad-manifest-format.prdx");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        var manifest = new PrdxManifest(
            "BAD",
            PrdxManifest.CurrentFormatVersion,
            "prj_demo_001",
            1,
            "2026-08-14T19:00:00Z",
            "2026-08-14T19:00:00Z",
            "test",
            PrdxManifest.ProjectPayloadPath,
            Sha256ForTest(projectBytes));

        WriteEntry(archive, PrdxManifest.ManifestPath, manifest.ToJsonObject().ToJsonString(new() { WriteIndented = true }));
        WriteEntry(archive, PrdxManifest.ProjectPayloadPath, Encoding.UTF8.GetString(projectBytes));
        return path;
    }

    public static CanonicalProject WithMissingPadReference()
    {
        var project = LoadFixtureProject().DeepClone();
        var endpoint = project.Root["logicalDesign"]?["netlist"]?["nets"]?[0]?["endpoints"]?[0] as JsonObject
            ?? throw new InvalidOperationException("Fixture endpoint not found.");
        endpoint["padId"] = "pad_missing";
        return project;
    }

    private static void WriteEntry(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(text);
    }

    private static string Sha256ForTest(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
}
