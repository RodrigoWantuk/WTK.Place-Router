using PlaceRouter.Application.Projects;

namespace PlaceRouter.DesignExchange.Prdx;

public static class PrdxRuntime
{
    public static ProjectService CreateProjectService()
    {
        var schemas = new SchemaRegistry();
        var validator = new CanonicalIntegrityValidator(schemas);
        var reader = new PrdxProjectReader(schemas, validator);
        var writer = new PrdxProjectWriter(reader, validator);
        return new ProjectService(reader, writer, validator);
    }
}
