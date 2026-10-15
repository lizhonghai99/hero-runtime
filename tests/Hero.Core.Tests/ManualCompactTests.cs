using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Xunit;

namespace Hero.Core.Tests;

public sealed class ManualCompactTests
{
    [Fact]
    public async Task Pipeline_AlwaysTrigger_KeepsLastTurns()
    {
        var messages = new List<ChatMessage>();
        for (var i = 0; i < 10; i++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user-{i}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant-{i}"));
        }

        CompactionStrategy strategy = new PipelineCompactionStrategy(
            new ToolResultCompactionStrategy(CompactionTriggers.Always, minimumPreservedGroups: 2),
            new SlidingWindowCompactionStrategy(CompactionTriggers.Always, minimumPreservedTurns: 3));

        var compacted = (await CompactionProvider.CompactAsync(strategy, messages)).ToList();

        Assert.True(compacted.Count < messages.Count);
        Assert.Contains(compacted, m => m.Text == "user-9");
        Assert.DoesNotContain(compacted, m => m.Text == "user-0");
    }
}
