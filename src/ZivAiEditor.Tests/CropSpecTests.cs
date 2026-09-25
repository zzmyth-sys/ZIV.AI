using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Session;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 1 (P1 · /扩图 relocation): <see cref="CropSpec.IsOutpaint"/> and the
/// <c>source_width</c> / <c>source_height</c> persistence round-trip. Agent / file IO only;
/// no GPU.
/// </summary>
public class CropSpecTests
{
    private static string NewRoot()
        => Path.Combine(Path.GetTempPath(), "zivai_cropspec_" + Guid.NewGuid().ToString("N"));

    private static string WriteFile(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        return path;
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

    private static CropSpec Rect(int x, int y, int w, int h, int sourceW, int sourceH)
        => new() { X = x, Y = y, Width = w, Height = h, SourceWidth = sourceW, SourceHeight = sourceH };

    [Fact]
    public void IsOutpaint_Inner_Crop_Is_False()
    {
        Assert.False(Rect(10, 10, 100, 100, 200, 200).IsOutpaint());
    }

    [Fact]
    public void IsOutpaint_Exact_Edges_Are_Inner()
    {
        // Sitting exactly on the source bounds counts as an inner crop (tolerance).
        Assert.False(Rect(0, 0, 200, 200, 200, 200).IsOutpaint());
    }

    [Theory]
    [InlineData(-1, 0, 200, 200)]   // extends left
    [InlineData(0, -1, 200, 200)]   // extends up
    [InlineData(0, 0, 201, 200)]    // extends right
    [InlineData(0, 0, 200, 201)]    // extends down
    public void IsOutpaint_Outside_Is_True(int x, int y, int w, int h)
    {
        Assert.True(Rect(x, y, w, h, 200, 200).IsOutpaint());
    }

    [Fact]
    public void IsOutpaint_Unknown_Source_Size_Is_False()
    {
        // Legacy crop (source size not persisted) must never be treated as an outpaint.
        Assert.False(Rect(-50, -50, 400, 400, 0, 0).IsOutpaint());
        Assert.False(Rect(-50, -50, 400, 400, 200, 0).IsOutpaint());
    }

    [Fact]
    public async Task Save_Then_Load_RoundTrips_Source_Size()
    {
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            var rootNode = session.GetHistory()[0];

            session.SetNodeCrop(rootNode.NodeId, new CropSpec
            {
                X = -12, Y = -7, Width = 60, Height = 45,
                SourceWidth = 48, SourceHeight = 32,
                ResultImagePath = WriteFile(source, "crop.png"),
            });

            await store.SaveAsync(session, "外扩");
            var loaded = await store.LoadAsync(session.SessionId);

            var crop = loaded.Session.GetHistory().Single(n => n.NodeId == rootNode.NodeId).Crop;
            Assert.NotNull(crop);
            Assert.Equal(48, crop!.SourceWidth);
            Assert.Equal(32, crop.SourceHeight);
            Assert.True(crop.IsOutpaint());
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public async Task Load_Legacy_Crop_Without_Source_Size_Defaults_To_Zero()
    {
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            var rootNode = session.GetHistory()[0];
            session.SetNodeCrop(rootNode.NodeId, new CropSpec
            {
                X = -12, Y = -7, Width = 60, Height = 45,
                SourceWidth = 48, SourceHeight = 32,
                ResultImagePath = WriteFile(source, "crop.png"),
            });

            await store.SaveAsync(session, "legacy");
            var path = Path.Combine(root, session.SessionId, "session.json");
            var json = await File.ReadAllTextAsync(path);
            json = System.Text.RegularExpressions.Regex.Replace(json, "\"source_width\"\\s*:\\s*\\d+,?", "");
            json = System.Text.RegularExpressions.Regex.Replace(json, "\"source_height\"\\s*:\\s*\\d+,?", "");
            await File.WriteAllTextAsync(path, json);

            var loaded = await store.LoadAsync(session.SessionId);
            var crop = loaded.Session.GetHistory().Single(n => n.NodeId == rootNode.NodeId).Crop;
            Assert.NotNull(crop);
            Assert.Equal(0, crop!.SourceWidth);
            Assert.Equal(0, crop.SourceHeight);
            Assert.False(crop.IsOutpaint());
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }
}
