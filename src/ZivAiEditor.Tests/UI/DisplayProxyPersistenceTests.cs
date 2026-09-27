using System;
using System.IO;
using System.Linq;
using SkiaSharp;
using Xunit;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.App.Imaging;
using ZivAiEditor.Imaging;

namespace ZivAiEditor.Tests;

/// <summary>
/// Project-save proxy wiring (8K fix): <c>WriteNodeProxies</c> must drop a sibling
/// <c>{nodeId}_proxy.png</c> (and crop proxy) beside the project originals, so reopening the
/// project never re-decodes the full-size image. Pure file IO + CPU Skia (no GPU, Z29).
/// </summary>
[Collection(DisplayProxyCollection.Name)]
public sealed class DisplayProxyPersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "zivai-proxysave-" + Guid.NewGuid().ToString("N"));

    public DisplayProxyPersistenceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        ProxyImageCache.CleanupAll();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WritePng(string name, int width, int height)
    {
        var path = Path.Combine(_dir, name);
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(120, 40, 220));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    [Fact]
    public void WriteNodeProxies_Writes_A_Sibling_Proxy_For_A_Large_Node_Image()
    {
        var session = new EditSession();
        session.SetRoot(WritePng("root.png", 4000, 1000)); // large root -> proxy needed
        session.AppendNode(null, WritePng("out.png", 800, 600), "/test"); // small output -> no proxy
        var nodes = session.GetHistory();

        DisplayProxyPersistence.WriteNodeProxies(nodes, _dir);

        foreach (var node in nodes)
        {
            var proxy = Path.Combine(_dir, node.NodeId + "_proxy.png");
            var (width, height) = ProxyImageCache.ReadPixelSize(node.ImagePath);
            if (Math.Max(width, height) > IDisplayImageLoader.MaxDisplaySide)
            {
                Assert.True(File.Exists(proxy), $"missing proxy for {node.NodeId}");
                using var decoded = SKBitmap.Decode(proxy);
                Assert.True(Math.Max(decoded!.Width, decoded.Height) <= IDisplayImageLoader.MaxDisplaySide);
            }
            else
            {
                Assert.False(File.Exists(proxy), $"unexpected proxy for small node {node.NodeId}");
            }
        }
    }

    [Fact]
    public void WriteNodeProxies_Is_A_NoOp_For_A_Missing_Directory()
    {
        var session = new EditSession();
        session.SetRoot(WritePng("root2.png", 4000, 1000));

        // Must not throw.
        DisplayProxyPersistence.WriteNodeProxies(
            session.GetHistory(), Path.Combine(_dir, "does-not-exist"));
    }

    [Fact]
    public void DeleteNodeArtifacts_Removes_Project_Proxies()
    {
        // The project cleanup's `{nodeId}_*.png` glob must sweep the proxy files too.
        var sessionId = Guid.NewGuid().ToString("N");
        var store = new SessionStore(_dir);
        var projectDirectory = Path.Combine(_dir, sessionId);
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllBytes(Path.Combine(projectDirectory, "n1.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(projectDirectory, "n1_proxy.png"), new byte[] { 2 });
        File.WriteAllBytes(Path.Combine(projectDirectory, "n1_crop_proxy.png"), new byte[] { 3 });

        store.DeleteNodeArtifacts(sessionId, new[] { "n1" }, includeReferences: false);

        Assert.False(File.Exists(Path.Combine(projectDirectory, "n1.png")));
        Assert.False(File.Exists(Path.Combine(projectDirectory, "n1_proxy.png")));
        Assert.False(File.Exists(Path.Combine(projectDirectory, "n1_crop_proxy.png")));
    }
}
