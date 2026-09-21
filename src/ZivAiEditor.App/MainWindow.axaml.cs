using Avalonia.Controls;
using ZivAiEditor.Contracts.Inference;

namespace ZivAiEditor.App;

public partial class MainWindow : Window
{
    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(IInferenceClient? inferenceClient)
    {
        InferenceClient = inferenceClient;
        InitializeComponent();
    }

    /// <summary>
    /// The inference entry point injected by <see cref="AppContext"/> (Step 4
    /// wiring only; UI presentation arrives in a later step).
    /// </summary>
    public IInferenceClient? InferenceClient { get; }
}
