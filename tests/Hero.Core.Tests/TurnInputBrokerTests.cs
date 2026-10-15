using Hero.Cli;
using Xunit;

namespace Hero.Core.Tests;

public sealed class TurnInputBrokerTests
{
    [Fact]
    public async Task PriorityReader_GetsLine_InsteadOfSteerPump()
    {
        var broker = new TurnInputBroker();
        broker.BeginPump();
        try
        {
            var priority = broker.ReadLineAsync();
            await Task.Delay(20);
            Assert.False(priority.IsCompleted);

            Assert.True(broker.TryDeliverPriority("2"));
            Assert.Equal("2", await priority);
        }
        finally
        {
            broker.EndPump();
        }
    }

    [Fact]
    public void WithoutPump_TryDeliverPriority_IsFalse()
    {
        var broker = new TurnInputBroker();
        Assert.False(broker.TryDeliverPriority("1"));
    }
}
