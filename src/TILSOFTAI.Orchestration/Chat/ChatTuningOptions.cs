namespace TILSOFTAI.Orchestration.Chat;

/// <summary>
/// Centralized tuning knobs for LLM behavior.
/// Keep temperatures low to reduce hallucination and reliance on prompts.
/// </summary>
public sealed class ChatTuningOptions
{
    /// <summary>
    /// Max number of tool-calling iterations per user turn (planner loop).
    /// Recommended: 4 - 10.
    /// </summary>
    public int MaxToolSteps { get; set; } = 8;

    /// <summary>
    /// Default max_tokens for OpenAI-compatible chat completions when the client does not provide one.
    /// </summary>
    public int MaxTokens { get; set; } = 1200;

    /// <summary>
    /// Temperature for tool-calling turns (FunctionChoiceBehavior.Auto).
    /// Recommended: 0.0 - 0.2.
    /// </summary>
    public double ToolCallTemperature { get; set; } = 0.1;

    /// <summary>
    /// Temperature for final synthesis when tools already returned evidence.
    /// Recommended: 0.1 - 0.3.
    /// </summary>
    public double SynthesisTemperature { get; set; } = 0.2;

    /// <summary>
    /// Temperature for plain chat when no tools are available/needed.
    /// Recommended: 0.2 - 0.5.
    /// </summary>
    public double NoToolsTemperature { get; set; } = 0.3;

    /// <summary>
    /// Max characters included in routing text for follow-up questions.
    /// </summary>
    public int MaxRoutingContextChars { get; set; } = 1200;
}
