using TidyBox.Core.Elevation;

namespace TidyBox.Core.Tests.Elevation;

public class PipeMessageFramingTests
{
    [Fact]
    public async Task WriteThenRead_RoundTripsMessage()
    {
        var stream = new MemoryStream();
        await PipeMessageFraming.WriteAsync(stream, new ElevationRequest("echo", "{\"x\":1}"));
        stream.Position = 0;

        var restored = await PipeMessageFraming.ReadAsync<ElevationRequest>(stream);

        Assert.Equal(new ElevationRequest("echo", "{\"x\":1}"), restored);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16 * 1024 * 1024 + 1)]
    public async Task ReadAsync_InvalidLength_ThrowsInvalidData(int length)
    {
        var stream = new MemoryStream(BitConverter.GetBytes(length));

        await Assert.ThrowsAsync<InvalidDataException>(() => PipeMessageFraming.ReadAsync<ElevationRequest>(stream));
    }

    [Fact]
    public async Task ReadAsync_TruncatedBody_ThrowsEndOfStream()
    {
        var bytes = BitConverter.GetBytes(100).Concat(new byte[] { 1, 2, 3 }).ToArray();

        await Assert.ThrowsAsync<EndOfStreamException>(() => PipeMessageFraming.ReadAsync<ElevationRequest>(new MemoryStream(bytes)));
    }
}
