using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PlaceRouter.DesignExchange.Prdx;

namespace PlaceRouter.DesignExchange.Tests;

internal static class PrdxTestData
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

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

    public static string FullFixtureProjectJsonPath =>
        Path.Combine(RepositoryRoot, "schemas", "prdx", "0.1", "examples", "minimal-2layer.project.json");

    public static string IncompleteFixtureProjectJsonPath =>
        Path.Combine(RepositoryRoot, "schemas", "prdx", "0.1", "examples", "incomplete-project.project.json");

    public static string FullProjectJson() => File.ReadAllText(FullFixtureProjectJsonPath);

    public static string IncompleteProjectJson() => File.ReadAllText(IncompleteFixtureProjectJsonPath);

    public static JsonObject FullProjectNode() => JsonNode.Parse(FullProjectJson())!.AsObject();

    public static string CreateFixturePrdx(string directory, string? projectJson = null, Action<JsonObject>? mutateManifest = null, IEnumerable<(string Path, byte[] Bytes)>? extras = null)
    {
        var json = projectJson ?? FullProjectJson();
        var payloadBytes = Utf8.GetBytes(json);
        var project = JsonNode.Parse(json)!.AsObject();
        var metadata = project["metadata"]!.AsObject();
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".prdx");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        var fingerprints = (project["sourceImports"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(source => new ManifestSourceFingerprint(
                source["id"]!.GetValue<string>(),
                source["sourceSha256"]!.GetValue<string>()))
            .ToArray();

        var manifest = new PrdxManifest(
            PrdxManifest.ExpectedFormat,
            PrdxManifest.CurrentFormatVersion,
            project["projectId"]!.GetValue<string>(),
            project["projectRevision"]!.GetValue<long>(),
            metadata["createdAt"]!.GetValue<string>(),
            metadata["modifiedAt"]!.GetValue<string>(),
            "test",
            PrdxManifest.ProjectPayloadPath,
            Sha256(payloadBytes),
            [],
            fingerprints).ToJsonObject();

        mutateManifest?.Invoke(manifest);
        WriteEntry(archive, PrdxManifest.ManifestPath, Utf8.GetBytes(manifest.ToJsonString(new() { WriteIndented = true }) + "\n"));
        WriteEntry(archive, PrdxManifest.ProjectPayloadPath, payloadBytes);

        foreach (var extra in extras ?? [])
        {
            WriteEntry(archive, extra.Path, extra.Bytes);
        }

        return path;
    }

    public static string CreatePrdxWithProjectBytes(string directory, byte[] bytes, Action<JsonObject>? mutateManifest = null)
    {
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".prdx");
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
            Sha256(bytes)).ToJsonObject();
        mutateManifest?.Invoke(manifest);
        WriteEntry(archive, PrdxManifest.ManifestPath, Utf8.GetBytes(manifest.ToJsonString(new() { WriteIndented = true }) + "\n"));
        WriteEntry(archive, PrdxManifest.ProjectPayloadPath, bytes);
        return path;
    }

    public static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static void WriteEntry(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(bytes);
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "placerouter-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
        }
    }
}
