using PlaceRouter.Core.Primitives;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Core.Results;

namespace PlaceRouter.Core.Tests;

public sealed class CorePrimitiveTests
{
    [Fact]
    public void Length_units_use_micrometer_canonical_unit()
    {
        Assert.Equal(1_250, LengthUnits.FromMillimeters(1.25m).Value);
        Assert.Equal(100, LengthUnits.FromMicrometers(100).Value);
    }

    [Fact]
    public void Operation_success_depends_only_on_blocking_diagnostics()
    {
        var warning = OperationResult<int>.Ok(1, [Diagnostic.Warning("TEST-WARN", "Test", "nonblocking")]);
        var blockingWarning = OperationResult<int>.Ok(1, [Diagnostic.Warning("TEST-BLOCK", "Test", "blocking", blocking: true)]);
        var error = OperationResult<int>.Ok(1, [Diagnostic.Error("TEST-ERROR", "Test", "error")]);

        Assert.True(warning.Success);
        Assert.False(blockingWarning.Success);
        Assert.False(error.Success);
    }
}
