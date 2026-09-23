using ZivAiEditor.Agent;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 8 session export tests (App layer, file IO only, no GPU).</summary>
public class SessionExporterTests
{
    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "zivai_export_" + Guid.NewGuid().ToString("N"));

    private static string WriteSourceImage(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        return path;
    }

    [Fact]
    public async Task Export_Writes_Json_And_Copies_Images()
    {
        var sourceDir = NewTempDir();
        var outputDir = NewTempDir();
        var exporter = new SessionExporter();

        try
        {
            var root = WriteSourceImage(sourceDir, "root.png");
            var nodeImage = WriteSourceImage(sourceDir, "node.png");

            var session = new EditSession();
            session.SetRoot(root);
            var rootNode = session.GetHistory()[0];
            var node = session.AppendNode(null, nodeImage, "/去水印");

            var result = await exporter.ExportAsync(session, outputDir);

            Assert.Equal(outputDir, result);
            Assert.True(File.Exists(Path.Combine(outputDir, "session.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, rootNode.NodeId + ".png")));
            Assert.True(File.Exists(Path.Combine(outputDir, node.NodeId + ".png")));

            var json = await File.ReadAllTextAsync(Path.Combine(outputDir, "session.json"));
            Assert.Contains(rootNode.NodeId, json);
            Assert.Contains(node.NodeId, json);
            Assert.Contains("/去水印", json);
        }
        finally
        {
            Cleanup(sourceDir);
            Cleanup(outputDir);
        }
    }

    [Fact]
    public async Task Export_Handles_Empty_Session()
    {
        var outputDir = NewTempDir();
        var exporter = new SessionExporter();

        try
        {
            var session = new EditSession();

            var result = await exporter.ExportAsync(session, outputDir);

            Assert.Equal(outputDir, result);
            var jsonPath = Path.Combine(outputDir, "session.json");
            Assert.True(File.Exists(jsonPath));

            var json = await File.ReadAllTextAsync(jsonPath);
            Assert.Contains("\"nodes\": []", json);
        }
        finally
        {
            Cleanup(outputDir);
        }
    }

    [Fact]
    public async Task Export_Failure_Does_Not_Throw()
    {
        var tempDir = NewTempDir();
        var exporter = new SessionExporter();

        try
        {
            // A file where a directory is expected makes CreateDirectory fail.
            var blockingFile = WriteSourceImage(tempDir, "blocker");
            var session = new EditSession();
            session.SetRoot(WriteSourceImage(tempDir, "root.png"));

            var result = await exporter.ExportAsync(session, blockingFile);

            Assert.Null(result);
        }
        finally
        {
            Cleanup(tempDir);
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
