using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Backend;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Task 3: the C# IPC frame-length cap matches Python (256 MiB). A header just above the old
/// 64 MiB cap must pass the length guard (and fail only on a truncated body), while a header
/// above 256 MiB is still rejected. (Named without "Ipc" so it counts in the non-GPU suite.)
/// </summary>
public class FrameLengthTests
{
    private static MemoryStream LengthOnlyStream(int declaredLength)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, declaredLength);
        return new MemoryStream(header);
    }

    [Fact]
    public void MaxFrameBytes_Matches_Python_Cap()
        => Assert.Equal(256 * 1024 * 1024, IpcFraming.MaxFrameBytes);

    [Fact]
    public async Task Frame_Just_Above_Old_64MiB_Cap_Passes_The_Length_Guard()
    {
        // 65 MiB > old 64 MiB cap, < new 256 MiB. The header is all the stream has, so the read
        // fails as TRUNCATED (EndOfStream) — proving the length guard did NOT reject it.
        using var stream = LengthOnlyStream(65 * 1024 * 1024);

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => IpcFraming.ReadFrameAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Frame_Above_256MiB_Is_Still_Rejected()
    {
        using var stream = LengthOnlyStream(256 * 1024 * 1024 + 1);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => IpcFraming.ReadFrameAsync(stream, CancellationToken.None));
    }
}
