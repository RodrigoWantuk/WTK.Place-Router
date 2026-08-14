using System.Text.Json;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.DesignExchange.Prdx;

return PlaceRouterCli.Run(args);

internal static class PlaceRouterCli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "validate" => Validate(args.Skip(1).ToArray(), inspect: false),
                "inspect" => Validate(args.Skip(1).ToArray(), inspect: true),
                _ => Unknown(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"INTERNAL ERROR: {ex.Message}");
            return 3;
        }
    }

    private static int Validate(string[] args, bool inspect)
    {
        var json = args.Contains("--json", StringComparer.Ordinal);
        var file = args.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
        if (file is null)
        {
            Console.Error.WriteLine("Missing PRDX file path.");
            return 2;
        }

        var service = PrdxRuntime.CreateProjectService();
        var result = service.LoadProject(file);

        if (json)
        {
            var payload = new
            {
                valid = result.Success,
                diagnostics = result.Diagnostics.Select(ToDto),
                summary = result.Project?.Summary
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
        else if (result.Success && result.Project is not null)
        {
            var summary = result.Project.Summary;
            Console.WriteLine("VALID");
            Console.WriteLine($"project: {summary.Name}");
            Console.WriteLine($"components: {summary.Components}");
            Console.WriteLine($"nets: {summary.Nets}");
            Console.WriteLine($"layers: {summary.Layers}");
            if (inspect)
            {
                Console.WriteLine($"footprints: {summary.Footprints}");
                Console.WriteLine($"component poses: {summary.ComponentPoses}");
                Console.WriteLine($"routes: {summary.Routes}");
                Console.WriteLine($"vias: {summary.Vias}");
                Console.WriteLine($"revision: {summary.ProjectRevision}");
            }
        }
        else
        {
            Console.WriteLine("INVALID");
            foreach (var diagnostic in result.Diagnostics)
            {
                Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
            }
        }

        return result.Success ? 0 : 2;
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

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("placerouter validate <file.prdx> [--json]");
        Console.WriteLine("placerouter inspect <file.prdx> [--json]");
    }
}
