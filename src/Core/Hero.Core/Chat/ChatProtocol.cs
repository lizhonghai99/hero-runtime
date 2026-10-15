namespace Hero.Core.Chat;

/// <summary>
/// Wire protocol used to talk to the model provider.
/// </summary>
public enum ChatProtocol
{
    /// <summary>OpenAI-compatible Responses API (<c>/v1/responses</c>).</summary>
    OpenAiResponses = 0,

    /// <summary>Anthropic Messages API (native Claude / many China gateways).</summary>
    Anthropic = 1,

    /// <summary>
    /// OpenAI-compatible Chat Completions (<c>/v1/chat/completions</c>).
    /// Prefer for gateways that do not correctly chain Responses <c>previous_response_id</c>
    /// (e.g. DeepSeek) — otherwise multi-turn streaming may keep replaying the first user turn.
    /// </summary>
    OpenAiChatCompletions = 2,
}
