using System;
using System.IO;
using System.Text.Json;
using ZivAiEditor.Agent.Project;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Project;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Bridge D1: source_image persistence — save/load, rename preservation, legacy fallback.</summary>
public class SourceImagePersistenceTests
{
    private static string TempRoot()
        => Path.Combine(Path.GetTempPath(), "zivai_src_" + Guid.NewGuid().ToString("N"));

    private static string WriteImage(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return path;
    }

    [Fact]
    public async Task Save_WritesSourceImage_And_KeepsFormatVersion2()
    {
        var root = TempRoot();
        var source = TempRoot();
        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteImage(source, "root.png"));

            await store.SaveAsync(session, "我的项目");

            var json = File.ReadAllText(
                Path.Combine(root, session.SessionId, "session.json"));
            using var document = JsonDocument.Parse(json);
            Assert.Equal(2, document.RootElement.GetProperty("version").GetInt32());
            Assert.Equal(
                Path.GetFullPath(session.SourceImage!),
                Path.GetFullPath(document.RootElement.GetProperty("source_image").GetString()!));

            var loaded = await store.LoadAsync(session.SessionId);
            Assert.Equal(session.SourceImage, loaded.Session.SourceImage);
        }
        finally
        {
            Cleanup(root, source);
        }
    }

    [Fact]
    public async Task Rename_PreservesSourceImage()
    {
        var root = TempRoot();
        var source = TempRoot();
        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteImage(source, "root.png"));
            await store.SaveAsync(session, "旧名");

            await store.WriteMetadataNameAsync(Path.Combine(root, session.SessionId), "新名");

            using var document = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(root, session.SessionId, "session.json")));
            Assert.Equal("新名", document.RootElement.GetProperty("name").GetString());
            Assert.True(document.RootElement.TryGetProperty("source_image", out _));
        }
        finally
        {
            Cleanup(root, source);
        }
    }

    [Fact]
    public void Legacy_WithoutSourceImage_LoadsNull()
    {
        var directory = TempRoot();
        try
        {
            WriteImage(directory, "root.png");
            var json = """
            {
              "version": 2,
              "name": "legacy",
              "session_id": "s1",
              "current_node_id": "n1",
              "created_at": "2026-01-01T00:00:00+08:00",
              "nodes": [
                {
                  "node_id": "n1",
                  "image_paths": ["root.png"],
                  "command": "原图",
                  "created_at": "2026-01-01T00:00:00+08:00"
                }
              ]
            }
            """;

            var result = SessionLoader.LoadFromJson(json, directory);

            Assert.Null(result.Session.SourceImage);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void ProjectSummary_PrimaryConstructor_IsUnchanged()
    {
        var summary = new ProjectSummary("s1", "n1", DateTimeOffset.Now);

        Assert.Null(summary.SourceImage);
        Assert.Equal("s1", summary.SessionId);
    }

    private static void Cleanup(params string[] directories)
    {
        foreach (var directory in directories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
