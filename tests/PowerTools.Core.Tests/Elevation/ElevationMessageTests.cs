using System.Text.Json;
using Xunit;
using PowerTools.Core.Elevation;

namespace PowerTools.Core.Tests.Elevation;

public class ElevationMessageTests
{
    [Fact]
    public void ElevationRequest_RoundTripsThroughJson()
    {
        var original = new ElevationRequest("uninstall.removeResidual", "{\"path\":\"C:\\\\Foo\"}");
        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<ElevationRequest>(json);

        Assert.Equal(original.OperationType, restored!.OperationType);
        Assert.Equal(original.PayloadJson, restored.PayloadJson);
    }

    [Fact]
    public void ElevationResponse_FailureCase_RoundTripsThroughJson()
    {
        var original = new ElevationResponse(false, "access denied", null);
        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<ElevationResponse>(json);

        Assert.False(restored!.Success);
        Assert.Equal("access denied", restored.ErrorMessage);
        Assert.Null(restored.ResultJson);
    }
}
