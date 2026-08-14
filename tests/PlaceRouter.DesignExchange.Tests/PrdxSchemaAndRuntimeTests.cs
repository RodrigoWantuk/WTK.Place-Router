using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using PlaceRouter.Application.Projects;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.DesignExchange.Prdx;
using PlaceRouter.Domain.Prdx;

namespace PlaceRouter.DesignExchange.Tests;

public sealed class PrdxSchemaAndRuntimeTests
{
    [Fact]
    public void Schema_fixture_validates()
    {
        var project = PrdxTestData.LoadFixtureProject();
        var validator = new CanonicalIntegrityValidator();

        var result = validator.Validate(project);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Load_valid_fixture_prdx_produces_expected_domain_summary()
    {
        using var temp = new TempDirectory();
        var prdx = PrdxTestData.CreateFixturePrdx(temp.Path);

        var result = new PrdxProjectReader().Load(prdx);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Project);
        Assert.Equal(1, result.Project.Summary.Components);
        Assert.Equal(2, result.Project.Summary.Nets);
        Assert.Equal(2, result.Project.Summary.Layers);
    }

    [Fact]
    public void Round_trip_load_save_load_preserves_semantic_summary()
    {
        using var temp = new TempDirectory();
        var source = PrdxTestData.CreateFixturePrdx(temp.Path);
        var reader = new PrdxProjectReader();
        var writer = new PrdxProjectWriter(reader);

        var loaded = reader.Load(source);
        Assert.True(loaded.Success);

        var roundTrip = Path.Combine(temp.Path, "roundtrip.prdx");
        var save = writer.Save(loaded.Project!, roundTrip);
        Assert.True(save.Success, string.Join(Environment.NewLine, save.Diagnostics.Select(d => d.Message)));

        var reopened = reader.Load(roundTrip);
        Assert.True(reopened.Success, string.Join(Environment.NewLine, reopened.Diagnostics.Select(d => d.Message)));
        Assert.Equal(loaded.Project!.Summary, reopened.Project!.Summary);
        Assert.Equal(loaded.Project.Root.ToJsonString(), reopened.Project.Root.ToJsonString());
    }

    [Fact]
    public void Incorrect_payload_hash_fails_with_expected_diagnostic()
    {
        using var temp = new TempDirectory();
        var prdx = PrdxTestData.CreatePrdxWithWrongHash(temp.Path);

        var result = new PrdxProjectReader().Load(prdx);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.PayloadHash);
    }

    [Fact]
    public void Bad_manifest_format_fails_with_schema_diagnostic()
    {
        using var temp = new TempDirectory();
        var prdx = PrdxTestData.CreatePrdxWithBadManifestFormat(temp.Path);

        var result = new PrdxProjectReader().Load(prdx);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.ManifestSchema);
    }

    [Fact]
    public void Missing_cross_reference_fails_integrity_validation()
    {
        var project = PrdxTestData.WithMissingPadReference();

        var result = new CanonicalIntegrityValidator().Validate(project);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.RefNotFound);
    }

    [Fact]
    public void Atomic_save_does_not_replace_existing_project_when_commit_fails()
    {
        using var temp = new TempDirectory();
        var path = PrdxTestData.CreateFixturePrdx(temp.Path);
        var originalBytes = File.ReadAllBytes(path);
        var writer = new PrdxProjectWriter();
        var project = CanonicalProjectFactory.CreateEmpty("Replacement", "prj_replacement");

        var result = writer.Save(project, path, new PrdxWriteOptions(_ => throw new IOException("simulated failure")));

        Assert.False(result.Success);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.True(new PrdxProjectReader().Load(path).Success);
    }

    [Fact]
    public async Task Cli_validate_returns_success_for_valid_and_invalid_for_bad_hash()
    {
        using var temp = new TempDirectory();
        var valid = PrdxTestData.CreateFixturePrdx(temp.Path);
        var invalid = PrdxTestData.CreatePrdxWithWrongHash(temp.Path);

        var validExit = await RunCli("validate", valid);
        var invalidExit = await RunCli("validate", invalid);

        Assert.Equal(0, validExit);
        Assert.Equal(2, invalidExit);
    }

    private static async Task<int> RunCli(params string[] args)
    {
        var projectPath = Path.Combine(PrdxTestData.RepositoryRoot, "src", "PlaceRouter.Cli", "PlaceRouter.Cli.csproj");
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(projectPath);
        psi.ArgumentList.Add("--");
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("dotnet process did not start.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private sealed class TempDirectory : IDisposable
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
                // Test temp cleanup is best effort.
            }
        }
    }
}
