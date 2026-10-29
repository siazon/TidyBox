using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TidyBox.Core.Elevation;

/// <summary>
/// Length-prefix framing for JSON messages over a byte-mode pipe: a 4-byte
/// little-endian length followed by that many UTF-8 JSON bytes. Needed because
/// JsonSerializer cannot detect the end of a value on a stream without EOF.
/// </summary>
public static class PipeMessageFraming
{
    private const int MaxMessageBytes = 16 * 1024 * 1024;

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken ct = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message);
        var buffer = new byte[4 + json.Length];
        BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), json.Length);
        json.CopyTo(buffer, 4);
        await stream.WriteAsync(buffer, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct);
        var length = BitConverter.ToInt32(header);
        if (length < 0 || length > MaxMessageBytes)
        {
            throw new InvalidDataException($"invalid message length: {length}");
        }

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct);
        return JsonSerializer.Deserialize<T>(body);
    }
}
