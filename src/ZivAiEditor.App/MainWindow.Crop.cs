using ZivAiEditor.App.Controls;

namespace ZivAiEditor.App;

/// <summary>
/// Crop half of <see cref="MainWindow"/> (Step 9C.4): routes a confirmed crop from the
/// preview window into the session. Split out of the main file to keep each file under
/// the Z8 line budget.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Appends a confirmed crop (Step 9C.4) to the session: the parent node is the one
    /// showing the cropped image, so the new "裁切" node hangs off it and becomes current.
    /// </summary>
    private void OnPreviewCropCompleted(object? sender, CropCompletedEventArgs e)
    {
        _vm.AppendEditNode(e.SourceImagePath, e.OutputPath, "裁切");
    }
}
