using SkiaSharp;
using Xunit;
using ZivAiEditor.UI.Imaging;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 9C.4 image-crop tests (ZIV.Imaging decode / encode + Skia subset, file IO only,
/// no GPU). Validates the output-path rule, collision avoidance, and that the crop
/// writes a new file of the right size without touching the source (Z24).
/// </summary>
public class ImageCropperTests
{
    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "zivai_crop_" + Guid.NewGuid().ToString("N"));

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

    [Fact]
    public void ResolveOutputPath_Uses_Source_Directory_And_Crop_Suffix()
    {
        var dir = NewTempDir();
        try
        {
            var source = Path.Combine(dir, "photo.png");
            var now = new DateTimeOffset(2026, 9, 23, 14, 5, 6, TimeSpan.Zero);

            var output = ImageCropper.ResolveOutputPath(source, now);

            Assert.Equal(dir, Path.GetDirectoryName(output));
            Assert.Equal("photo_crop_20260923_140506.png", Path.GetFileName(output));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void ResolveOutputPath_Avoids_Collisions()
    {
        var dir = NewTempDir();
        try
        {
            var source = Path.Combine(dir, "photo.png");
            var now = new DateTimeOffset(2026, 9, 23, 14, 5, 6, TimeSpan.Zero);

            var first = ImageCropper.ResolveOutputPath(source, now);
            File.WriteAllBytes(first, new byte[] { 1 });

            var second = ImageCropper.ResolveOutputPath(source, now);
            File.WriteAllBytes(second, new byte[] { 1 });

            var third = ImageCropper.ResolveOutputPath(source, now);

            Assert.Equal("photo_crop_20260923_140506.png", Path.GetFileName(first));
            Assert.Equal("photo_crop_20260923_140506_1.png", Path.GetFileName(second));
            Assert.Equal("photo_crop_20260923_140506_2.png", Path.GetFileName(third));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task CropAsync_Writes_New_Png_With_Correct_Size_And_Leaves_Source()
    {
        var dir = NewTempDir();
        try
        {
            var source = WritePng(dir, "photo.png", 100, 80);
            var before = await File.ReadAllBytesAsync(source);

            var output = await ImageCropper.CropAsync(source, 10, 10, 50, 40);

            Assert.NotNull(output);
            Assert.NotEqual(source, output);
            Assert.True(File.Exists(output));

            using var decoded = SKBitmap.Decode(output);
            Assert.Equal(50, decoded.Width);
            Assert.Equal(40, decoded.Height);

            // Source bytes are byte-for-byte unchanged (Z24).
            var after = await File.ReadAllBytesAsync(source);
            Assert.Equal(before, after);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task CropAsync_Clamps_Rectangle_To_Image()
    {
        var dir = NewTempDir();
        try
        {
            var source = WritePng(dir, "photo.png", 40, 30);

            // Request extends past the right / bottom edges; the result is clipped.
            var output = await ImageCropper.CropAsync(source, 20, 10, 100, 100);

            Assert.NotNull(output);
            using var decoded = SKBitmap.Decode(output);
            Assert.Equal(20, decoded.Width);
            Assert.Equal(20, decoded.Height);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task CropAsync_Missing_Source_Returns_Null()
    {
        var dir = NewTempDir();
        try
        {
            var missing = Path.Combine(dir, "nope.png");
            Assert.Null(await ImageCropper.CropAsync(missing, 0, 0, 10, 10));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public async Task CropAsync_Empty_Rectangle_Returns_Null()
    {
        var dir = NewTempDir();
        try
        {
            var source = WritePng(dir, "photo.png", 100, 80);
            Assert.Null(await ImageCropper.CropAsync(source, 10, 10, 0, 40));
        }
        finally
        {
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
