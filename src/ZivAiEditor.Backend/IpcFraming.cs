using System.Buffers.Binary;
using System.Text;

namespace ZivAiEditor.Backend;

internal static class IpcFraming
{
    public const byte FrameJson = 0x01;
    public const byte FrameBinary = 0x02;
    // Task 3: match Python's cap (ipc.py:12 / config.py:63 = 256 MiB) so a large (4K + multi-ref)
    // frame is not rejected by C# while Python already sends it. Raising the cap is not a
    // behavioral downgrade; local IPC is not a network attack surface.
    public const int MaxFrameBytes = 256 * 1024 * 1024;

    private static readonly byte[] ShutdownFrame = Encoding.UTF8.GetBytes("{\"type\":\"shutdown\"}");

    public static async Task WriteFrameAsync(
        Stream stream,
        byte frameType,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct)
    {
        var header = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), payload.Length + 1);
        header[4] = frameType;
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        }

        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static Task WriteJsonAsync(Stream stream, string json, CancellationToken ct)
        => WriteFrameAsync(stream, FrameJson, Encoding.UTF8.GetBytes(json), ct);

    public static async Task<(byte FrameType, byte[] Payload)?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 1 || length > MaxFrameBytes)
        {
            throw new InvalidDataException($"Invalid IPC frame length: {length}.");
        }

        var body = new byte[length];
        if (!await ReadExactAsync(stream, body, ct).ConfigureAwait(false))
        {
            throw new EndOfStreamException("Truncated IPC frame.");
        }

        var payload = new byte[length - 1];
        Array.Copy(body, 1, payload, 0, payload.Length);
        return (body[0], payload);
    }

    public static async Task TryWriteShutdownAsync(Stream stream)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await WriteFrameAsync(stream, FrameJson, ShutdownFrame, timeout.Token).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static async Task<bool> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], ct).ConfigureAwait(false);
            if (read == 0)
            {
                if (offset == 0)
                {
                    return false;
                }

                throw new EndOfStreamException("Truncated IPC frame.");
            }

            offset += read;
        }

        return true;
    }
}
