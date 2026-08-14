using PlaceRouter.Domain.Prdx;

namespace PlaceRouter.Domain.Tests;

public sealed class CanonicalProjectFactoryTests
{
    [Fact]
    public void Empty_project_has_required_prdx_sections()
    {
        var project = CanonicalProjectFactory.CreateEmpty("New Board", "prj_test");

        Assert.Equal("prj_test", project.ProjectId);
        Assert.Equal("New Board", project.Name);
        Assert.Equal(2, project.Summary.Layers);
        Assert.Equal(0, project.Summary.Components);
    }
}
