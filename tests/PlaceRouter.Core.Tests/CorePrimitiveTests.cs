using PlaceRouter.Core.Primitives;

namespace PlaceRouter.Core.Tests;

public sealed class CorePrimitiveTests
{
    [Fact]
    public void Length_units_use_micrometer_canonical_unit()
    {
        Assert.Equal(1_250, LengthUnits.FromMillimeters(1.25m).Value);
        Assert.Equal(100, LengthUnits.FromMicrometers(100).Value);
    }
}
