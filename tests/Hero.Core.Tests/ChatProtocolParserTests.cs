using Hero.Core.Chat;
using Hero.Core.Providers;

namespace Hero.Core.Tests;

public sealed class ChatProtocolParserTests
{
    [Theory]
    [InlineData("openai-responses", ChatProtocol.OpenAiResponses)]
    [InlineData("responses", ChatProtocol.OpenAiResponses)]
    [InlineData("openai", ChatProtocol.OpenAiResponses)]
    [InlineData("openai-chat", ChatProtocol.OpenAiChatCompletions)]
    [InlineData("chat-completions", ChatProtocol.OpenAiChatCompletions)]
    [InlineData("anthropic", ChatProtocol.Anthropic)]
    [InlineData("claude", ChatProtocol.Anthropic)]
    [InlineData("messages", ChatProtocol.Anthropic)]
    public void TryParse_KnownAliases(string value, ChatProtocol expected)
    {
        Assert.True(ChatProtocolParser.TryParse(value, out var protocol));
        Assert.Equal(expected, protocol);
    }

    [Fact]
    public void InferFromEndpoint_DetectsAnthropicPath()
    {
        Assert.Equal(
            ChatProtocol.Anthropic,
            ChatProtocolParser.InferFromEndpoint("https://api.moonshot.cn/anthropic"));
        Assert.Null(ChatProtocolParser.InferFromEndpoint("https://api.deepseek.com/v1"));
    }

    [Fact]
    public void Resolve_PrefersExplicitThenProviderThenEndpoint()
    {
        Assert.Equal(
            ChatProtocol.OpenAiResponses,
            ChatProtocolParser.Resolve(
                "openai-responses",
                ChatProtocol.Anthropic,
                "https://api.moonshot.cn/anthropic"));

        Assert.Equal(
            ChatProtocol.Anthropic,
            ChatProtocolParser.Resolve(
                null,
                ChatProtocol.Anthropic,
                "https://api.moonshot.cn/v1"));

        Assert.Equal(
            ChatProtocol.Anthropic,
            ChatProtocolParser.Resolve(
                null,
                null,
                "https://api.moonshot.cn/anthropic"));

        Assert.Equal(
            ChatProtocol.OpenAiResponses,
            ChatProtocolParser.Resolve(null, null, "https://api.deepseek.com/v1"));
    }

    [Fact]
    public void MoonshotCatalog_DefaultsToAnthropic()
    {
        var moonshot = LlmProviderCatalog.Find("moonshot")!;
        Assert.Equal(ChatProtocol.Anthropic, moonshot.DefaultProtocol);
        Assert.Equal("https://api.moonshot.cn/anthropic", moonshot.DefaultEndpoint);

        var deepseek = LlmProviderCatalog.Find("deepseek")!;
        Assert.Equal(ChatProtocol.OpenAiResponses, deepseek.DefaultProtocol);
        Assert.EndsWith("/v1", deepseek.DefaultEndpoint);
    }

    [Fact]
    public void EndpointNormalizer_RewritesMoonshotV1ToAnthropic()
    {
        var moonshot = LlmProviderCatalog.Find("moonshot")!;
        Assert.Equal(
            "https://api.moonshot.cn/anthropic",
            ChatEndpointNormalizer.Normalize(
                "https://api.moonshot.cn/v1",
                ChatProtocol.Anthropic,
                moonshot));

        Assert.Equal(
            "https://api.deepseek.com/v1",
            ChatEndpointNormalizer.Normalize(
                "https://api.deepseek.com",
                ChatProtocol.OpenAiResponses,
                LlmProviderCatalog.Find("deepseek")));
    }
}
