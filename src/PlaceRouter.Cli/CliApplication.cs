using System.Text.Json;
using PlaceRouter.Application.Projects;
using PlaceRouter.Core.Diagnostics;

namespace PlaceRouter.Cli;

public sealed class CliApplication(
    ProjectService projectService,
    TextWriter stdout,
    TextWriter stderr,
    string version)
{
    public int Run(string[] args)
    {
        try
        {
            return RunCore(args);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"INTERNAL ERROR: {ex.Message}");
            return 3;
        }
    }

    private int RunCore(string[] args)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            PrintUsage();
            return 0;
        }

        if (args is ["--version"])
        {
            stdout.WriteLine(version);
            return 0;
        }

        return args[0] switch
        {
            "validate" => Execute(args[1..], inspect: false),
            "project-check" => Execute(args[1..], inspect: false),
            "inspect" => Execute(args[1..], inspect: true),
            "import-dsn" => ImportDsn(args[1..]),
            _ => UsageError($"Unknown command '{args[0]}'.")
        };
    }

    private int ImportDsn(string[] args)
    {
        string? source = null;
        string? output = null;
        var policy = SourceRetentionPolicy.ReferenceOnly;
        var json = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--out":
                    if (++i >= args.Length)
                    {
                        return UsageError("Missing value for --out.");
                    }

                    output = args[i];
                    break;
                case "--embed-source":
                    policy = SourceRetentionPolicy.Embed;
                    break;
                case "--reference-source":
                    policy = SourceRetentionPolicy.ReferenceOnly;
                    break;
                case "--no-source":
                    policy = SourceRetentionPolicy.None;
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        return UsageError($"Unknown option '{arg}'.");
                    }

                    if (source is not null)
                    {
                        return UsageError($"Unexpected extra argument '{arg}'.");
                    }

                    source = arg;
                    break;
            }
        }

        if (source is null)
        {
            return UsageError("Missing DSN source path.");
        }

        if (output is null)
        {
            return UsageError("Missing --out <file.prdx>.");
        }

        var import = projectService.ImportDesign(new ImportRequest(source, policy));
        if (!import.Success || import.Document is null)
        {
            WriteImportResult(import, json, saved: false);
            return 2;
        }

        var save = projectService.SaveProject(import.Document, output);
        var combinedDiagnostics = import.Diagnostics.Concat(save.Diagnostics).ToArray();
        var result = import with { Diagnostics = combinedDiagnostics };
        WriteImportResult(result, json, saved: save.Success);
        return save.Success && !combinedDiagnostics.HasBlockingDiagnostics() ? 0 : 2;
    }

    private int Execute(string[] args, bool inspect)
    {
        var json = false;
        string? file = null;

        foreach (var arg in args)
        {
            if (arg == "--json")
            {
                if (json)
                {
                    return UsageError("Duplicate --json option.");
                }

                json = true;
                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal))
            {
                return UsageError($"Unknown option '{arg}'.");
            }

            if (file is not null)
            {
                return UsageError($"Unexpected extra argument '{arg}'.");
            }

            file = arg;
        }

        if (file is null)
        {
            return UsageError("Missing PRDX file path.");
        }

        var result = projectService.LoadProject(file);
        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(ToJson(result), new JsonSerializerOptions { WriteIndented = true }));
        }
        else if (result.Success && result.Project is not null)
        {
            var summary = result.Project.Summary;
            stdout.WriteLine("VALID");
            stdout.WriteLine($"project: {summary.Name}");
            stdout.WriteLine($"components: {summary.Components}");
            stdout.WriteLine($"nets: {summary.Nets}");
            stdout.WriteLine($"layers: {summary.Layers}");
            if (inspect)
            {
                stdout.WriteLine($"footprints: {summary.Footprints}");
                stdout.WriteLine($"component poses: {summary.ComponentPoses}");
                stdout.WriteLine($"routes: {summary.Routes}");
                stdout.WriteLine($"vias: {summary.Vias}");
                stdout.WriteLine($"revision: {summary.ProjectRevision}");
                stdout.WriteLine($"format: {result.Document?.FileContext.FormatVersion}");
                stdout.WriteLine($"schema: {result.Project.SchemaVersion}");
            }
        }
        else
        {
            stdout.WriteLine("INVALID");
            foreach (var diagnostic in result.Diagnostics)
            {
                stdout.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
            }
        }

        return result.Success ? 0 : 2;
    }

    private object ToJson(ProjectLoadResult result) => new
    {
        valid = result.Success,
        diagnostics = result.Diagnostics.Select(ToDto),
        summary = result.Project?.Summary,
        formatVersion = result.Document?.FileContext.FormatVersion,
        schemaVersion = result.Project?.SchemaVersion
    };

    private void WriteImportResult(ImportResult result, bool json, bool saved)
    {
        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new
            {
                imported = result.Success,
                saved,
                diagnostics = result.Diagnostics.Select(ToDto),
                capabilities = result.Capabilities,
                summary = result.Project?.Summary,
                sourceFingerprint = result.SourceFingerprint
            }, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        stdout.WriteLine(result.Success && saved ? "IMPORTED" : "IMPORT FAILED");
        if (result.Project is not null)
        {
            var summary = result.Project.Summary;
            stdout.WriteLine($"project: {summary.Name}");
            stdout.WriteLine($"components: {summary.Components}");
            stdout.WriteLine($"nets: {summary.Nets}");
            stdout.WriteLine($"layers: {summary.Layers}");
        }

        foreach (var capability in result.Capabilities.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            stdout.WriteLine($"capability {capability.Key}: {capability.Value}");
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            stdout.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
        }
    }

    private static object ToDto(Diagnostic diagnostic) => new
    {
        diagnostic.Code,
        severity = diagnostic.Severity.ToString().ToUpperInvariant(),
        diagnostic.Category,
        diagnostic.Message,
        diagnostic.Blocking,
        entityRefs = diagnostic.EntityRefs ?? []
    };

    private int UsageError(string message)
    {
        stderr.WriteLine(message);
        PrintUsage(stderr);
        return 2;
    }

    private void PrintUsage() => PrintUsage(stdout);

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("placerouter validate <file.prdx> [--json]");
        writer.WriteLine("placerouter project-check <file.prdx> [--json]");
        writer.WriteLine("placerouter inspect <file.prdx> [--json]");
        writer.WriteLine("placerouter import-dsn <source.dsn> --out <file.prdx> [--embed-source|--reference-source|--no-source] [--json]");
        writer.WriteLine("placerouter --help");
        writer.WriteLine("placerouter --version");
    }
}
