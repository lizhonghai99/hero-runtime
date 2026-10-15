using System.Collections.Generic;
using System.Linq;
using Hero.Core.Agent;
using Microsoft.Extensions.AI;
using Xunit;

namespace Hero.Core.Tests;

/// <summary>
/// Regresses the cross-turn tool-history wipe: after a tool-using turn, Hero must
/// keep FunctionCall/FunctionResult messages in the transcript that the next turn
/// replays (clearHistory + full list), not overwrite with text-only user/assistant.
/// </summary>
public sealed class TranscriptCommitTests
{
    [Fact]
    public void CommitSuccessfulTurn_PrefersSessionHistory_WithToolMessages()
    {
        var call = new FunctionCallContent("call-1", "read_file", new Dictionary<string, object?>
        {
            ["path"] = "foo.txt",
        });
        var result = new FunctionResultContent("call-1", "hello from foo");
        var sessionHistory = new List<ChatMessage>
        {
            new(ChatRole.User, "read foo.txt"),
            new(ChatRole.Assistant, [call]),
            new(ChatRole.Tool, [result]),
            new(ChatRole.Assistant, "foo.txt says hello from foo"),
        };

        // What Hero held mid-turn before commit (user only; assistant text not yet appended).
        var transcript = new List<ChatMessage>
        {
            new(ChatRole.User, "read foo.txt"),
        };

        MafAgentRuntime.CommitSuccessfulTurn(
            transcript,
            sessionHistory,
            assistantText: "foo.txt says hello from foo",
            out var writeBack);

        Assert.Null(writeBack);
        Assert.Equal(4, transcript.Count);
        Assert.Contains(transcript, m => m.Contents.OfType<FunctionCallContent>().Any());
        Assert.Contains(transcript, m => m.Contents.OfType<FunctionResultContent>().Any(fr =>
            fr.Result?.ToString() == "hello from foo"));
    }

    [Fact]
    public void CommitSuccessfulTurn_TwoTurnReplay_KeepsPriorToolResult()
    {
        var turn1History = new List<ChatMessage>
        {
            new(ChatRole.User, "read foo.txt"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "read_file")]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "secret-payload")]),
            new(ChatRole.Assistant, "done"),
        };

        var transcript = new List<ChatMessage> { new(ChatRole.User, "read foo.txt") };
        MafAgentRuntime.CommitSuccessfulTurn(transcript, turn1History, "done", out _);

        // Turn 2: same protocol as RunTurnStreamingAsync — append user, then CopyTranscript.
        transcript.Add(new ChatMessage(ChatRole.User, "what did the file contain?"));
        var replay = transcript.ToList();

        Assert.Contains(replay, m => m.Contents.OfType<FunctionResultContent>().Any(fr =>
            fr.Result?.ToString() == "secret-payload"));
        Assert.Equal(ChatRole.User, replay[^1].Role);
        Assert.Equal("what did the file contain?", replay[^1].Text);
    }

    [Fact]
    public void CommitSuccessfulTurn_OldTextOnlyOverwrite_WouldDropTools()
    {
        // Documents the pre-fix bug: replacing session with text-only transcript.
        var sessionHistory = new List<ChatMessage>
        {
            new(ChatRole.User, "read foo.txt"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "read_file")]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "secret-payload")]),
            new(ChatRole.Assistant, "done"),
        };

        var textOnly = new List<ChatMessage>
        {
            new(ChatRole.User, "read foo.txt"),
            new(ChatRole.Assistant, "done"),
        };

        Assert.Contains(sessionHistory, m => m.Contents.OfType<FunctionResultContent>().Any());
        Assert.DoesNotContain(textOnly, m => m.Contents.OfType<FunctionResultContent>().Any());
    }

    [Fact]
    public void CommitSuccessfulTurn_Fallback_WhenSessionHistoryEmpty()
    {
        var transcript = new List<ChatMessage>
        {
            new(ChatRole.User, "hi"),
        };

        MafAgentRuntime.CommitSuccessfulTurn(
            transcript,
            sessionHistory: null,
            assistantText: "hello",
            out var writeBack);

        Assert.NotNull(writeBack);
        Assert.Equal(2, transcript.Count);
        Assert.Equal("hello", transcript[^1].Text);
        Assert.Same(writeBack![0], transcript[0]);
    }
}
