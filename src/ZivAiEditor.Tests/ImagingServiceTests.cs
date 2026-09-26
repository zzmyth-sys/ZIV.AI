using Xunit;
using ZivAiEditor.Imaging;

namespace ZivAiEditor.Tests;

/// <summary>
/// ImagingService.CleanupNode tests (node deletion): per-node crop + mask temp files, idempotent
/// / missing no-op, never throws. File IO only, no GPU.
/// </summary>
public class ImagingServiceTests
{
    [Fact]
    public void CleanupNode_Deletes_Crop_And_Mask_Files()
    {
        var session = Guid.NewGuid().ToString("N");
        var crop = ImageCropper.ResolveCropPath(session, "n1");
        var mask = MaskExporter.ResolveMaskPath(session, "n1");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(crop)!);
            Directory.CreateDirectory(Path.GetDirectoryName(mask)!);
            File.WriteAllBytes(crop, new byte[] { 1 });
            File.WriteAllBytes(mask, new byte[] { 2 });

            new ImagingService().CleanupNode(session, new[] { "n1" });

            Assert.False(File.Exists(crop));
            Assert.False(File.Exists(mask));
        }
        finally
        {
            ImageCropper.CleanupSession(session);
            MaskExporter.CleanupSession(session);
        }
    }

    [Fact]
    public void CleanupNode_Missing_Or_Blank_Is_NoOp_And_Does_Not_Throw()
    {
        var service = new ImagingService();

        service.CleanupNode(null, new[] { "n1" });
        service.CleanupNode("   ", new[] { "n1" });
        service.CleanupNode("missing-session", Array.Empty<string>());
        service.CleanupNode("missing-session", new[] { "n1", "  " });
    }
}
