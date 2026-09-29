using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;
using ZivAiEditor.App.Controls;

namespace ZivAiEditor.Tests.UI;

/// <summary>
/// Headless tests for <see cref="LoraControl"/> (no Python / GPU / real window). The control's
/// public surface (LoadFrom / CurrentState / RequestSave / RequestPickFileAsync) is reachable
/// without opening the Flyout, so the state round-trip and the save / pick wiring are testable.
/// </summary>
public class LoraControlTests
{
    [Fact]
    public void LoadFrom_RoundTrips_Through_CurrentState()
    {
        HeadlessTest.Run(() =>
        {
            var control = new LoraControl();
            control.LoadFrom(new LoraUiState { Enabled = true, Strength = 1.5, Path = "face-swap", DisplayName = "换头" });

            var state = control.CurrentState;
            Assert.True(state.Enabled);
            Assert.Equal(1.5, state.Strength);
            Assert.Equal("face-swap", state.Path);
        });
    }

    [Fact]
    public void RequestSave_Raises_SaveRequested_With_Current_State()
    {
        HeadlessTest.Run(() =>
        {
            var control = new LoraControl();
            control.LoadFrom(new LoraUiState { Enabled = true, Strength = 0.75, Path = "p" });

            LoraUiState? captured = null;
            control.SaveRequested += (_, state) => captured = state;
            control.RequestSave();

            Assert.NotNull(captured);
            Assert.True(captured!.Enabled);
            Assert.Equal(0.75, captured.Strength);
            Assert.Equal("p", captured.Path);
        });
    }

    [Fact]
    public void Picking_A_File_Updates_The_Path()
    {
        HeadlessTest.Run(() =>
        {
            var control = new LoraControl { FilePicker = () => Task.FromResult<string?>(@"C:\lora\x.safetensors") };
            control.LoadFrom(new LoraUiState { Enabled = false, Strength = 1.0, Path = "" });

            control.RequestPickFileAsync().GetAwaiter().GetResult();

            Assert.Equal(@"C:\lora\x.safetensors", control.CurrentState.Path);
        });
    }

    [Fact]
    public void Cancelled_Pick_Keeps_The_Path()
    {
        HeadlessTest.Run(() =>
        {
            var control = new LoraControl { FilePicker = () => Task.FromResult<string?>(null) };
            control.LoadFrom(new LoraUiState { Enabled = true, Strength = 1.0, Path = "face-swap" });

            control.RequestPickFileAsync().GetAwaiter().GetResult();

            Assert.Equal("face-swap", control.CurrentState.Path);
        });
    }

    [Fact]
    public void Flyout_Closed_With_Change_Raises_SaveRequested()
    {
        HeadlessTest.Run(() =>
        {
            var control = new LoraControl();
            var window = new Window { Content = control };
            window.Show();
            try
            {
                control.LoadFrom(new LoraUiState { Enabled = true, Strength = 1.0, Path = "a" });
                var button = control.FindControl<Button>("PART_Btn")!;
                var flyout = button.Flyout!;

                // Open snapshots the initial state; the edit while open must trigger a save on close.
                flyout.ShowAt(button);
                Dispatcher.UIThread.RunJobs();
                control.LoadFrom(new LoraUiState { Enabled = true, Strength = 1.5, Path = "a" });

                LoraUiState? captured = null;
                control.SaveRequested += (_, state) => captured = state;
                flyout.Hide();
                Dispatcher.UIThread.RunJobs();

                Assert.NotNull(captured);
                Assert.Equal(1.5, captured!.Strength);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Flyout_Closed_Without_Change_Does_Not_Raise()
    {
        HeadlessTest.Run(() =>
        {
            var control = new LoraControl();
            var window = new Window { Content = control };
            window.Show();
            try
            {
                control.LoadFrom(new LoraUiState { Enabled = true, Strength = 1.0, Path = "a" });
                var button = control.FindControl<Button>("PART_Btn")!;
                var flyout = button.Flyout!;

                flyout.ShowAt(button);
                Dispatcher.UIThread.RunJobs();

                var raised = false;
                control.SaveRequested += (_, _) => raised = true;
                flyout.Hide();
                Dispatcher.UIThread.RunJobs();

                Assert.False(raised);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
