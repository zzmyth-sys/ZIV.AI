using SkiaSharp;
using Xunit;
using ZivAiEditor.UI.Imaging;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 9C.7 mask export tests: program-directory cache path, per-node overwrite, binary
/// 0 / 255 output (R2) and robust cleanup. File IO only, no GPU.
/// </summary>
public class MaskExporterTests
{
    private static string NewSessionId() => Guid.NewGuid().ToString("N");

    private static byte[] Pattern(int width, int height)
    {
        var pixels = new byte[width * height];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = 255;
        }

        return pixels;
    }

    [Fact]
    public void ResolveMaskPath_Is_Under_Program_Cache()
    {
        var path = MaskExporter.ResolveMaskPath("s1", "n1");

        Assert.Equal(
            Path.Combine(MaskExporter.MasksRootDirectory, "s1", "n1.png"),
            path);
        Assert.StartsWith(MaskExporter.MasksRootDirectory, path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_Writes_File_And_TryLoad_RoundTrips()
    {
        var session = NewSessionId();
        try
        {
            var pixels = Pattern(8, 6);

            var path = await MaskExporter.ExportAsync(session, "n1", pixels, 8, 6);

            Assert.Equal(MaskExporter.ResolveMaskPath(session, "n1"), path);
            Assert.True(File.Exists(path));

            var loaded = await MaskExporter.TryLoadAsync(path);
            Assert.NotNull(loaded);
            Assert.Equal(8, loaded!.Value.Width);
            Assert.Equal(6, loaded.Value.Height);
            Assert.Equal(pixels, loaded.Value.Pixels);
        }
        finally
        {
            MaskExporter.CleanupSession(session);
        }
    }

    [Fact]
    public async Task Exported_Png_Is_Binary_Per_Channel()
    {
        var session = NewSessionId();
        try
        {
            var path = await MaskExporter.ExportAsync(session, "n1", Pattern(8, 6), 8, 6);
            Assert.NotNull(path);

            using var decoded = SKBitmap.Decode(path);
            Assert.NotNull(decoded);
            for (var y = 0; y < decoded!.Height; y++)
            {
                for (var x = 0; x < decoded.Width; x++)
                {
                    var color = decoded.GetPixel(x, y);
                    Assert.True(color.Red is 0 or 255);
                    Assert.Equal(color.Red, color.Green);
                    Assert.Equal(color.Red, color.Blue);
                }
            }
        }
        finally
        {
            MaskExporter.CleanupSession(session);
        }
    }

    [Fact]
    public async Task Export_Blank_Ids_Or_Bad_Dims_Returns_Null()
    {
        var pixels = new byte[4];

        Assert.Null(await MaskExporter.ExportAsync("", "n1", pixels, 2, 2));
        Assert.Null(await MaskExporter.ExportAsync("s1", "   ", pixels, 2, 2));
        Assert.Null(await MaskExporter.ExportAsync("s1", "n1", pixels, 0, 2));
        Assert.Null(await MaskExporter.ExportAsync("s1", "n1", pixels, 2, 0));
        Assert.Null(await MaskExporter.ExportAsync("s1", "n1", new byte[1], 2, 2));
    }

    [Fact]
    public async Task Export_Same_Node_Overwrites_The_File()
    {
        var session = NewSessionId();
        try
        {
            var first = await MaskExporter.ExportAsync(session, "n1", Pattern(8, 6), 8, 6);

            var second = new byte[8 * 6];
            Array.Fill(second, (byte)255);
            var secondPath = await MaskExporter.ExportAsync(session, "n1", second, 8, 6);

            Assert.Equal(first, secondPath);

            var sessionDir = Path.Combine(MaskExporter.MasksRootDirectory, session);
            Assert.Single(Directory.GetFiles(sessionDir));

            var loaded = await MaskExporter.TryLoadAsync(secondPath);
            Assert.NotNull(loaded);
            Assert.Equal(second, loaded!.Value.Pixels);
        }
        finally
        {
            MaskExporter.CleanupSession(session);
        }
    }

    [Fact]
    public async Task TryLoad_Missing_File_Returns_Null()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "nope.png");

        Assert.Null(await MaskExporter.TryLoadAsync(path));
        Assert.Null(await MaskExporter.TryLoadAsync(""));
    }

    [Fact]
    public async Task CleanupSession_Removes_Directory_And_Is_Idempotent()
    {
        var session = NewSessionId();
        try
        {
            await MaskExporter.ExportAsync(session, "n1", Pattern(8, 6), 8, 6);
            var sessionDir = Path.Combine(MaskExporter.MasksRootDirectory, session);
            Assert.True(Directory.Exists(sessionDir));

            MaskExporter.CleanupSession(session);
            Assert.False(Directory.Exists(sessionDir));

            MaskExporter.CleanupSession(session);
            MaskExporter.CleanupSession(null);
            MaskExporter.CleanupSession("   ");
        }
        finally
        {
            MaskExporter.CleanupSession(session);
        }
    }

    [Fact]
    public async Task CleanupAll_Removes_Every_Session_Directory()
    {
        var sessionA = NewSessionId();
        var sessionB = NewSessionId();
        try
        {
            await MaskExporter.ExportAsync(sessionA, "n1", Pattern(8, 6), 8, 6);
            await MaskExporter.ExportAsync(sessionB, "n1", Pattern(8, 6), 8, 6);

            MaskExporter.CleanupAll();

            Assert.False(Directory.Exists(Path.Combine(MaskExporter.MasksRootDirectory, sessionA)));
            Assert.False(Directory.Exists(Path.Combine(MaskExporter.MasksRootDirectory, sessionB)));
        }
        finally
        {
            MaskExporter.CleanupSession(sessionA);
            MaskExporter.CleanupSession(sessionB);
        }
    }
}
