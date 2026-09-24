using SkiaSharp;
using Xunit;
using ZivAiEditor.Imaging;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 9C.6-B2 crop temp-file tests: program-directory cache path, per-node overwrite,
/// and robust cleanup. File IO only, no GPU.
/// </summary>
public class ImageCropperTests
{
    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "zivai_crop_" + Guid.NewGuid().ToString("N"));

    private static string NewSessionId() => Guid.NewGuid().ToString("N");

    private static string WritePng(string directory, string name, int width, int height)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static string WriteAlphaPng(string directory, string name, int width, int height)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        bitmap.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var paint = new SKPaint { Color = SKColors.CornflowerBlue };
            canvas.DrawRect(new SKRect(0, 0, width, height / 2f), paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [Fact]
    public void ResolveCropPath_Is_Under_Program_Cache()
    {
        var path = ImageCropper.ResolveCropPath("s1", "n1");

        Assert.Equal(
            Path.Combine(ImageCropper.CropsRootDirectory, "s1", "n1.png"),
            path);
        Assert.StartsWith(ImageCropper.CropsRootDirectory, path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CropAsync_Writes_To_Cache_And_Leaves_Source()
    {
        var dir = NewTempDir();
        var session = NewSessionId();
        try
        {
            var source = WritePng(dir, "photo.png", 100, 80);
            var before = await File.ReadAllBytesAsync(source);

            var output = await ImageCropper.CropAsync(session, "n1", source, 10, 10, 50, 40);

            Assert.Equal(ImageCropper.ResolveCropPath(session, "n1"), output);
            Assert.True(File.Exists(output));

            using var decoded = SKBitmap.Decode(output);
            Assert.Equal(50, decoded.Width);
            Assert.Equal(40, decoded.Height);

            // Source bytes are byte-for-byte unchanged (Z24).
            Assert.Equal(before, await File.ReadAllBytesAsync(source));
        }
        finally
        {
            ImageCropper.CleanupSession(session);
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task CropAsync_Recrop_Overwrites_The_Same_File()
    {
        var dir = NewTempDir();
        var session = NewSessionId();
        try
        {
            var source = WritePng(dir, "photo.png", 100, 80);

            var first = await ImageCropper.CropAsync(session, "n1", source, 10, 10, 50, 40);
            var second = await ImageCropper.CropAsync(session, "n1", source, 0, 0, 30, 20);

            Assert.Equal(first, second); // same per-node path — overwritten, not accumulated

            var sessionDir = Path.Combine(ImageCropper.CropsRootDirectory, session);
            Assert.Single(Directory.GetFiles(sessionDir)); // exactly one file for the node

            using var decoded = SKBitmap.Decode(second);
            Assert.Equal(30, decoded.Width);
            Assert.Equal(20, decoded.Height);
        }
        finally
        {
            ImageCropper.CleanupSession(session);
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task CropAsync_Expands_Canvas_With_Gray_Fill()
    {
        var dir = NewTempDir();
        var session = NewSessionId();
        try
        {
            var source = WritePng(dir, "photo.png", 40, 30);

            // Negative origin -> output canvas is the rect; source pasted at (10, 5).
            var output = await ImageCropper.CropAsync(session, "n1", source, -10, -5, 60, 40);

            Assert.NotNull(output);
            using var decoded = SKBitmap.Decode(output);
            Assert.Equal(60, decoded.Width);
            Assert.Equal(40, decoded.Height);

            // Grey 0.5 fill outside the source (D1).
            var gray = new SKColor(128, 128, 128);
            Assert.Equal(gray, decoded.GetPixel(0, 0));
            Assert.Equal(gray, decoded.GetPixel(59, 39));
            Assert.Equal(gray, decoded.GetPixel(0, 39));
            Assert.Equal(gray, decoded.GetPixel(59, 0));

            // Source pixels at the (-x, -y) offset.
            Assert.Equal(SKColors.CornflowerBlue, decoded.GetPixel(10, 5));
            Assert.Equal(SKColors.CornflowerBlue, decoded.GetPixel(49, 34));
        }
        finally
        {
            ImageCropper.CleanupSession(session);
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task CropAsync_Inner_Crop_Has_No_Fill()
    {
        var dir = NewTempDir();
        var session = NewSessionId();
        try
        {
            var source = WritePng(dir, "photo.png", 40, 30);

            // R5: legacy inner-crop data (positive X/Y) still behaves as before.
            var output = await ImageCropper.CropAsync(session, "n1", source, 5, 5, 20, 15);

            Assert.NotNull(output);
            using var decoded = SKBitmap.Decode(output);
            Assert.Equal(20, decoded.Width);
            Assert.Equal(15, decoded.Height);
            Assert.Equal(SKColors.CornflowerBlue, decoded.GetPixel(0, 0));
            Assert.Equal(SKColors.CornflowerBlue, decoded.GetPixel(19, 14));
        }
        finally
        {
            ImageCropper.CleanupSession(session);
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task CropAsync_Inner_Crop_Preserves_Source_Alpha()
    {
        var dir = NewTempDir();
        var session = NewSessionId();
        try
        {
            var source = WriteAlphaPng(dir, "alpha.png", 40, 30);

            var output = await ImageCropper.CropAsync(session, "n1", source, 0, 0, 20, 20);

            Assert.NotNull(output);
            using var decoded = SKBitmap.Decode(output);
            Assert.Equal(20, decoded.Width);
            Assert.Equal(20, decoded.Height);

            // Top half is opaque source; the transparent source region must stay transparent
            // (Src blend), not become the grey fill.
            Assert.Equal(SKColors.CornflowerBlue, decoded.GetPixel(5, 5));
            Assert.Equal(0, decoded.GetPixel(5, 18).Alpha);
        }
        finally
        {
            ImageCropper.CleanupSession(session);
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task CropAsync_Missing_Source_Returns_Null()
    {
        var dir = NewTempDir();
        var session = NewSessionId();
        try
        {
            var missing = Path.Combine(dir, "nope.png");
            Assert.Null(await ImageCropper.CropAsync(session, "n1", missing, 0, 0, 10, 10));
        }
        finally
        {
            ImageCropper.CleanupSession(session);
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task CropAsync_Empty_Rectangle_Returns_Null()
    {
        var dir = NewTempDir();
        var session = NewSessionId();
        try
        {
            var source = WritePng(dir, "photo.png", 100, 80);
            Assert.Null(await ImageCropper.CropAsync(session, "n1", source, 10, 10, 0, 40));
        }
        finally
        {
            ImageCropper.CleanupSession(session);
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task CropAsync_Blank_Ids_Return_Null()
    {
        var dir = NewTempDir();
        try
        {
            var source = WritePng(dir, "photo.png", 40, 30);

            Assert.Null(await ImageCropper.CropAsync("", "n1", source, 0, 0, 10, 10));
            Assert.Null(await ImageCropper.CropAsync("s1", "   ", source, 0, 0, 10, 10));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task CleanupSession_Removes_Directory_And_Is_Idempotent()
    {
        var dir = NewTempDir();
        var session = NewSessionId();
        try
        {
            var source = WritePng(dir, "photo.png", 40, 30);
            await ImageCropper.CropAsync(session, "n1", source, 0, 0, 20, 20);

            var sessionDir = Path.Combine(ImageCropper.CropsRootDirectory, session);
            Assert.True(Directory.Exists(sessionDir));

            ImageCropper.CleanupSession(session);
            Assert.False(Directory.Exists(sessionDir));

            // Second call is a no-op and must not throw.
            ImageCropper.CleanupSession(session);
            ImageCropper.CleanupSession(null);
            ImageCropper.CleanupSession("   ");
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task CleanupAll_Removes_Every_Session_Directory()
    {
        var dir = NewTempDir();
        var sessionA = NewSessionId();
        var sessionB = NewSessionId();
        try
        {
            var source = WritePng(dir, "photo.png", 40, 30);
            await ImageCropper.CropAsync(sessionA, "n1", source, 0, 0, 20, 20);
            await ImageCropper.CropAsync(sessionB, "n1", source, 0, 0, 20, 20);

            ImageCropper.CleanupAll();

            Assert.False(Directory.Exists(Path.Combine(ImageCropper.CropsRootDirectory, sessionA)));
            Assert.False(Directory.Exists(Path.Combine(ImageCropper.CropsRootDirectory, sessionB)));
        }
        finally
        {
            ImageCropper.CleanupSession(sessionA);
            ImageCropper.CleanupSession(sessionB);
            Cleanup(dir);
        }
    }

    private static void Cleanup(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
