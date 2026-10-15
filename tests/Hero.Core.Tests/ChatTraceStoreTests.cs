using Hero.Core.Observability;

namespace Hero.Core.Tests;

public sealed class ChatTraceStoreTests
{
    [Fact]
    public void GetTurnFromEnd_ReturnsLatestFirst()
    {
        var store = new ChatTraceStore();
        store.BeginTurn("a");
        store.AddCall(new ModelCallRecord { TurnId = "a", StartedAt = DateTimeOffset.UtcNow });
        store.EndTurn("a");

        store.BeginTurn("b");
        store.AddCall(new ModelCallRecord { TurnId = "b", StartedAt = DateTimeOffset.UtcNow });
        store.EndTurn("b");

        Assert.Equal("b", store.GetTurnFromEnd(1)!.TurnId);
        Assert.Equal("a", store.GetTurnFromEnd(2)!.TurnId);
        Assert.Null(store.GetTurnFromEnd(3));
    }

    [Fact]
    public void MaxTurns_EvictsOldest()
    {
        var store = new ChatTraceStore(maxTurns: 2);
        store.BeginTurn("1");
        store.EndTurn("1");
        store.BeginTurn("2");
        store.EndTurn("2");
        store.BeginTurn("3");
        store.EndTurn("3");

        Assert.Null(store.GetTurnFromEnd(3));
        Assert.Equal("3", store.GetTurnFromEnd(1)!.TurnId);
        Assert.Equal("2", store.GetTurnFromEnd(2)!.TurnId);
    }
}
