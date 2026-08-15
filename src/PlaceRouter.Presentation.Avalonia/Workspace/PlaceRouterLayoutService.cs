using System.Text.Json;

namespace PlaceRouter.Presentation.Workspace;

public sealed class PlaceRouterLayoutService
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;

    public PlaceRouterLayoutService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WTK.PlaceRouter",
            "workspace-layout.json");
    }

    public string PathOnDisk => _path;

    public PlaceRouterLayoutDocument Load()
    {
        if (!File.Exists(_path))
        {
            return new PlaceRouterLayoutDocument();
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<PlaceRouterLayoutDocument>(stream, Options) ?? new PlaceRouterLayoutDocument();
        }
        catch (JsonException)
        {
            return new PlaceRouterLayoutDocument();
        }
        catch (IOException)
        {
            return new PlaceRouterLayoutDocument();
        }
    }

    public void Save(PlaceRouterLayoutDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var stream = File.Create(_path);
        JsonSerializer.Serialize(stream, document, Options);
    }
}
