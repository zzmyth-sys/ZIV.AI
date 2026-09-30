using System;
using System.IO;
using System.Threading;
using Avalonia.Threading;
using SkiaSharp;
using Xunit;
using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Agent.Project;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.App;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Imaging;
using ZivAiEditor.Tools;
using ZivAiEditor.UI.Chat;
using ChatMessage = ZivAiEditor.UI.Chat.ChatMessage;

namespace ZivAiEditor.Tests.UI;

/// <summary>
/// B2: live preview frames are decoded <b>off</b> the UI thread (Z11) and coalesced to the newest
/// frame, so a fast producer cannot pile decodes onto the UI thread. Headless only — no Python /
/// GPU / real window; frames are synthesised with SkiaSharp.
/// </summary>
public class MainWindowPreviewTests
{
    private static MainWindow BuildWindow(EditSession session)
    {
        var parser = new CommandParser(new List<CommandDefinition>());
        var executor = new Executor(new ToolRegistry(), new ExecutionQueue(), session, session, parser);
        var store = new SessionStore(Path.Combine(
            Path.GetTempPath(), "zivai-preview-mw-" + Guid.NewGuid().ToString("N")));
        var projects = new ProjectService(store);
        var imaging = new ImagingService();
        var modelProfiles = new ModelProfileRegistry(modelsFilePath: null);
        var shell = new ShellService();

        return new MainWindow(
            session, session, parser, executor, store, projects, imaging, shell, modelProfiles);
    }

    /// <summary>A valid JPEG of the given side (gradient so the byte length grows with the side).</summary>
    private static byte[] Jpeg(int side, byte seed)
    {
        using var bitmap = new SKBitmap(side, side);
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                bitmap.SetPixel(x, y, new SKColor(
                    (byte)(x * 7 + seed), (byte)(y * 7 + seed), (byte)((x + y) * 3 + seed)));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    private static bool PumpUntil(Func<bool> condition, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return condition();
    }

    /// <summary>Adds an in-flight "生成中" bubble so <c>ShowPreview</c> has an Image target.</summary>
    private static void QueuePendingBubble(MainWindow window)
    {
        window.ViewModel.Messages.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Text = "生成中…",
            IsPending = true,
        });
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void Preview_Frame_Is_Applied_To_The_Pending_Bubble()
    {
        var session = new EditSession();

        HeadlessTest.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                window = BuildWindow(session);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                QueuePendingBubble(window);

                window.ShowPreview(Jpeg(8, 10));

                Assert.True(PumpUntil(() => window.PreviewApplyCount >= 1), "preview was not applied");
                Assert.True(window.HasPreviewImage);
                Assert.Equal(1, window.PreviewApplyCount);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void Preview_Decode_Runs_Off_The_UI_Thread()
    {
        var session = new EditSession();

        HeadlessTest.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                window = BuildWindow(session);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                QueuePendingBubble(window);

                var uiThreadId = Environment.CurrentManagedThreadId;
                window.ShowPreview(Jpeg(64, 20));

                Assert.True(PumpUntil(() => window.PreviewApplyCount >= 1), "preview was not applied");
                Assert.NotEqual(uiThreadId, window.LastPreviewDecodeThreadId);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void Rapid_Frames_Apply_Only_The_Latest()
    {
        var session = new EditSession();

        HeadlessTest.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                window = BuildWindow(session);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                QueuePendingBubble(window);

                var small = Jpeg(4, 30);
                var big = Jpeg(256, 200);
                Assert.True(big.Length > small.Length, $"{big.Length} !> {small.Length}");

                window.ShowPreview(small);
                window.ShowPreview(big);

                // Latest wins: the applied frame is the big (last) one, and the queue never grew
                // beyond the two frames in flight.
                Assert.True(PumpUntil(() => window.LastPreviewAppliedLength == big.Length),
                    "the latest frame was not applied");
                Assert.True(window.PreviewApplyCount <= 2, $"applied {window.PreviewApplyCount} frames");
            }
            finally
            {
                window?.Close();
            }
        });
    }
}
