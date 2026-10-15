using Hero.Core.Observability;
using Microsoft.Extensions.AI;

namespace Hero.Core.Tests;

public sealed class TurnBoundChatClientTests
{
    [Fact]
    public void EnsureExpectedUser_ReplacesStickyFirstUser()
    {
        IList<ChatMessage> stuck =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.User, "### Current todo list\n- none yet"),
        ];

        var fixedList = TurnBoundChatClient.EnsureExpectedUser(stuck, "你是谁啊");

        Assert.Equal(2, fixedList.Count);
        Assert.Equal("你是谁啊", fixedList[0].Text);
        Assert.StartsWith("###", fixedList[1].Text);
    }

    [Fact]
    public void EnsureExpectedUser_KeepsWhenAlreadyCorrect()
    {
        IList<ChatMessage> ok =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "hi"),
            new ChatMessage(ChatRole.User, "你是谁啊"),
        ];

        var fixedList = TurnBoundChatClient.EnsureExpectedUser(ok, "你是谁啊");
        Assert.Same(ok, fixedList);
    }

    [Fact]
    public void EnsureExpectedUser_AppendsWhenHistoryPresentWithoutRewritingEarlierUsers()
    {
        IList<ChatMessage> history =
        [
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "hi"),
            new ChatMessage(ChatRole.User, "当前的路径是?"),
            new ChatMessage(ChatRole.Assistant, @"D:\LearnCode\hero-runtime"),
            new ChatMessage(ChatRole.User, "### Current todo list\n- none yet"),
        ];

        var fixedList = TurnBoundChatClient.EnsureExpectedUser(history, "查一下wsl的版本");

        Assert.Equal("hello", fixedList[0].Text);
        Assert.Equal("当前的路径是?", fixedList[2].Text);
        Assert.Equal("查一下wsl的版本", fixedList[4].Text);
        Assert.StartsWith("###", fixedList[5].Text);
    }
}
