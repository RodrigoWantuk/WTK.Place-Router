using PlaceRouter.Domain.Model;

namespace PlaceRouter.Domain.Tests;

public sealed class CanonicalProjectFactoryTests
{
    [Fact]
    public void Incomplete_project_does_not_invent_board_geometry()
    {
        var project = CanonicalProjectFactory.CreateIncomplete("New Board", "prj_test");

        Assert.Equal("prj_test", project.ProjectId.Value);
        Assert.Equal("New Board", project.Metadata.Name);
        Assert.Null(project.Board.Outline);
        Assert.Empty(project.Board.Layers);
        Assert.Empty(project.Board.Stackup);
        Assert.Equal("INCOMPLETE", project.PhysicalDesignState.Status);
        Assert.Equal(0, project.Summary.Components);
    }
}
