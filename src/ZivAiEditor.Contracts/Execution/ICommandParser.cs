using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Session;

namespace ZivAiEditor.Contracts.Execution;

/// <summary>
/// Parses a user chat input into a single-step <see cref="EditPlan"/> without any
/// LLM (INTERACTION.md §2). An input that starts with "/" selects a command from
/// <c>Template/commands.json</c>; anything else is taken verbatim as the prompt.
///
/// The parser only produces plans — it never calls the Executor and never touches
/// the backend (Step 8 scope). It is deterministic and offline, so it can be used
/// as the first layer before the LLM-based rewriting lands.
/// </summary>
public interface ICommandParser
{
    /// <summary>
    /// Parses <paramref name="input"/> into an <see cref="EditPlan"/> using the
    /// current working image from <paramref name="session"/> as the source.
    /// </summary>
    Task<ParseResult> ParseAsync(string input, IEditSession session, CancellationToken ct = default);

    /// <summary>
    /// Parses <paramref name="input"/> as <see cref="ParseAsync(string, IEditSession, CancellationToken)"/>
    /// and, when the produced plan carries no resolution of its own, stamps
    /// <paramref name="resolution"/> onto <see cref="EditPlan.Resolution"/> (V3: moved
    /// down from the UI, which used to rebuild the whole plan). The UI-selected
    /// resolution therefore never overrides a resolution the parser already produced
    /// (e.g. a command-owned fixed resolution such as <c>/全景</c>). A <c>null</c>
    /// <paramref name="resolution"/> leaves the plan unchanged.
    /// </summary>
    Task<ParseResult> ParseAsync(
        string input,
        IEditSession session,
        ResolutionPolicy? resolution,
        CancellationToken ct = default);

    /// <summary>
    /// Same as <see cref="ParseAsync(string, IEditSession, ResolutionPolicy?, CancellationToken)"/>
    /// but also supplies the number of images in the pipeline (main + references), used to pick a
    /// command's single / multi template variant. Pass <c>-1</c> when the count is unknown (variant
    /// selection then falls back to the command's <c>defaultVariant</c>).
    /// </summary>
    Task<ParseResult> ParseAsync(
        string input,
        IEditSession session,
        int imageCount,
        ResolutionPolicy? resolution,
        CancellationToken ct = default);
}
