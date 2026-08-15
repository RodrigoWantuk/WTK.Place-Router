using PlaceRouter.Application.Projects;
using PlaceRouter.DesignExchange.Prdx;
using PlaceRouter.DesignExchange.Specctra;

namespace PlaceRouter.Infrastructure.Composition;

public static class PlaceRouterComposition
{
    public static ProjectService CreateProjectService()
    {
        var schemaValidator = new SchemaRegistry();
        var integrityValidator = new CanonicalIntegrityValidator();
        var store = new PrdxProjectStore(schemaValidator, integrityValidator);
        return new ProjectService(store, integrityValidator, [new SpecctraDsnImporter()]);
    }
}
