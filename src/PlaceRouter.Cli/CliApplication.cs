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
            "inspect" => Execute(args[1..], inspect: true),
            _ => UsageError($"Unknown command '{args[0]}'.")
        };
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
        writer.WriteLine("placerouter inspect <file.prdx> [--json]");
        writer.WriteLine("placerouter --help");
        writer.WriteLine("placerouter --version");
    }
}
