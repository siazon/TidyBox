using System.Text.Json;
using Xunit;
using TidyBox.Core.Elevation;

namespace TidyBox.Core.Tests.Elevation;

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
    public void ElevationResponse_SuccessCase_RoundTripsThroughJson()
    {
        var original = new ElevationResponse(true, null, "{\"removed\":3}");
        var restored = JsonSerializer.Deserialize<ElevationResponse>(JsonSerializer.Serialize(original));

        Assert.True(restored!.Success);
        Assert.Null(restored.ErrorMessage);
        Assert.Equal("{\"removed\":3}", restored.ResultJson);
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
