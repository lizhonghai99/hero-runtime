using Hero.Core.Observability;
using Hero.Core.Options;

namespace Hero.Core.Tests;

public sealed class ChatTracePersistTests
{
    [Fact]
    public async Task AddCall_PersistsToTracesJsonl_AndReloadGroupsByTurn()
    {
        var root = Directory.CreateTempSubdirectory("hero-traces-").FullName;
        try
        {
            var options = new HeroOptions
            {
                WorkingDirectory = root,
                TurnLogDirectory = Path.Combine(root, "logs"),
            };
            await using var store = new ChatTraceStore(options);
            store.BeginTurn("t1");
            store.AddCall(new ModelCallRecord
            {
                TurnId = "t1",
                AgentName = "hi",
                StartedAt = DateTimeOffset.UtcNow,
                ToolNames = ["run_shell"],
            });
            store.EndTurn("t1");
            store.BeginTurn("t2");
            store.AddCall(new ModelCallRecord
            {
                TurnId = "t2",
                AgentName = "ExploreAgent",
                StartedAt = DateTimeOffset.UtcNow,
            });
            await store.FlushPersistAsync();

            var path = store.GetTracesFilePath();
            Assert.True(File.Exists(path));
            Assert.EndsWith("traces.jsonl", path, StringComparison.OrdinalIgnoreCase);

            await using var reloadedStore = new ChatTraceStore(options);
            var reloaded = reloadedStore.LoadPersistedTurns();
            Assert.Equal(2, reloaded.Count);
            Assert.Equal("t1", reloaded[0].TurnId);
            Assert.Equal("hi", reloaded[0].Calls[0].AgentName);
            Assert.Equal("ExploreAgent", reloaded[1].Calls[0].AgentName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentAddCall_SerializesPersist_NoLostLines()
    {
        var root = Directory.CreateTempSubdirectory("hero-traces-conc-").FullName;
        try
        {
            var options = new HeroOptions
            {
                WorkingDirectory = root,
                TurnLogDirectory = Path.Combine(root, "logs"),
            };
            await using var store = new ChatTraceStore(options);
            store.BeginTurn("t-conc");

            const int n = 32;
            Parallel.For(0, n, i =>
            {
                store.AddCall(new ModelCallRecord
                {
                    TurnId = "t-conc",
                    AgentName = "agent-" + i,
                    StartedAt = DateTimeOffset.UtcNow,
                });
            });

            await store.FlushPersistAsync();

            var lines = (await File.ReadAllLinesAsync(store.GetTracesFilePath()))
                .Where(static l => !string.IsNullOrWhiteSpace(l))
                .ToList();
            Assert.Equal(n, lines.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FlushPersistAsync_BeforeLoad_IncludesLatestCall()
    {
        var root = Directory.CreateTempSubdirectory("hero-traces-flush-").FullName;
        try
        {
            var options = new HeroOptions
            {
                WorkingDirectory = root,
                TurnLogDirectory = Path.Combine(root, "logs"),
            };
            await using var store = new ChatTraceStore(options);
            store.BeginTurn("t-flush");
            store.AddCall(new ModelCallRecord
            {
                TurnId = "t-flush",
                AgentName = "hi",
                StartedAt = DateTimeOffset.UtcNow,
            });

            // Same order as AgentHost.ExportSessionAsync(html): flush then load.
            await store.FlushPersistAsync();
            var traces = store.LoadPersistedTurns(options.WorkingDirectory);
            Assert.Single(traces);
            Assert.Equal("t-flush", traces[0].TurnId);
            Assert.Equal("hi", traces[0].Calls[0].AgentName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeAsync_FlushesPendingLines()
    {
        var root = Directory.CreateTempSubdirectory("hero-traces-dispose-").FullName;
        try
        {
            var options = new HeroOptions
            {
                WorkingDirectory = root,
                TurnLogDirectory = Path.Combine(root, "logs"),
            };
            string path;
            await using (var store = new ChatTraceStore(options))
            {
                store.BeginTurn("t-dispose");
                store.AddCall(new ModelCallRecord
                {
                    TurnId = "t-dispose",
                    AgentName = "ExploreAgent",
                    StartedAt = DateTimeOffset.UtcNow,
                });
                path = store.GetTracesFilePath();
                // No explicit FlushPersistAsync — DisposeAsync must drain the queue.
            }

            Assert.True(File.Exists(path));
            var lines = (await File.ReadAllLinesAsync(path))
                .Where(static l => !string.IsNullOrWhiteSpace(l))
                .ToList();
            Assert.Single(lines);
            Assert.Contains("ExploreAgent", lines[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
