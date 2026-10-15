using System.Text.Json;
using Hero.Core.Options;
using Hero.Core.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hero.Core.Tests;

public sealed class SessionTreeStoreTests
{
    [Fact]
    public async Task Append_BuildsParentChain_AndTipMoves()
    {
        using var temp = new TempDir();
        var store = CreateStore(temp.Path);

        await store.BeginNewAsync(temp.Path, "s1");
        await store.AppendAsync(MakeAppend(temp.Path, "s1", "hello"));
        var tip1 = store.CurrentTipId;
        Assert.False(string.IsNullOrWhiteSpace(tip1));

        await store.AppendAsync(MakeAppend(temp.Path, "s1", "second"));
        var tip2 = store.CurrentTipId;
        Assert.NotEqual(tip1!, tip2);

        var tree = await store.GetTreeAsync(temp.Path);
        Assert.Equal(2, tree.Nodes.Count);
        Assert.Equal(tip2, tree.TipId);
        var second = tree.Nodes.Single(n => n.Id == tip2);
        Assert.Equal(tip1, second.ParentId);
        Assert.Contains("second", second.UserPreview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checkout_ChangesTip_AndBranchParent()
    {
        using var temp = new TempDir();
        var store = CreateStore(temp.Path);

        await store.BeginNewAsync(temp.Path, "s1");
        await store.AppendAsync(MakeAppend(temp.Path, "s1", "root"));
        var n1 = store.CurrentTipId!;
        await store.AppendAsync(MakeAppend(temp.Path, "s1", "child-a"));
        var n2 = store.CurrentTipId!;

        var loaded = await store.TryCheckoutAsync(temp.Path, n1[..4]);
        Assert.NotNull(loaded);
        Assert.Equal(n1, store.CurrentTipId);

        await store.AppendAsync(MakeAppend(temp.Path, "s1", "child-b"));
        var n3 = store.CurrentTipId!;
        var tree = await store.GetTreeAsync(temp.Path);
        var branch = tree.Nodes.Single(n => n.Id == n3);
        Assert.Equal(n1, branch.ParentId);
        Assert.NotEqual(n2, n3!);
    }

    [Fact]
    public async Task BeginNew_ArchivesExistingCurrentJsonl()
    {
        using var temp = new TempDir();
        var store = CreateStore(temp.Path);

        await store.BeginNewAsync(temp.Path, "s-old");
        await store.AppendAsync(MakeAppend(temp.Path, "s-old", "keep-me"));
        var oldPath = store.GetSessionFilePath(temp.Path);
        Assert.True(File.Exists(oldPath));
        var oldText = await File.ReadAllTextAsync(oldPath);

        await store.BeginNewAsync(temp.Path, "s-new");
        Assert.True(File.Exists(oldPath));
        var newText = await File.ReadAllTextAsync(oldPath);
        Assert.Contains("s-new", newText, StringComparison.Ordinal);
        Assert.DoesNotContain("keep-me", newText, StringComparison.Ordinal);

        var archiveDir = Path.Combine(Path.GetDirectoryName(oldPath)!, "archive");
        Assert.True(Directory.Exists(archiveDir));
        var archives = Directory.GetFiles(archiveDir, "*.jsonl");
        Assert.Single(archives);
        Assert.Contains("keep-me", await File.ReadAllTextAsync(archives[0]), StringComparison.Ordinal);
        Assert.Contains(oldText.Trim(), (await File.ReadAllTextAsync(archives[0])).Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyFile_WithoutNodeId_LoadsAsLinearChain()
    {
        using var temp = new TempDir();
        var key = SessionPath.GetDirectoryKey(temp.Path);
        var dir = Path.Combine(temp.Path, "sessions", key);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "current.jsonl");

        var sessionJson = JsonSerializer.SerializeToElement(new { messages = Array.Empty<object>() });
        await File.WriteAllTextAsync(
            path,
            $$"""
            {"t":"meta","sessionId":"legacy","cwd":"{{temp.Path.Replace("\\", "\\\\")}}"}
            {"t":"turn","sessionId":"legacy","user":"u1","assistant":"a1"}
            {"t":"maf","sessionId":"legacy","session":{{sessionJson}}}
            {"t":"turn","sessionId":"legacy","user":"u2","assistant":"a2"}
            {"t":"maf","sessionId":"legacy","session":{{sessionJson}}}

            """);

        var store = CreateStore(temp.Path);
        var snap = await store.TryLoadLatestAsync(temp.Path);
        Assert.NotNull(snap);

        var tree = await store.GetTreeAsync(temp.Path);
        Assert.True(tree.Nodes.Count >= 1);
        Assert.False(string.IsNullOrWhiteSpace(tree.TipId));
    }

    [Fact]
    public async Task TruncatedTrailingJsonl_DoesNotThrow_KeepsPriorNodes()
    {
        using var temp = new TempDir();
        var store = CreateStore(temp.Path);
        await store.BeginNewAsync(temp.Path, "s1");
        await store.AppendAsync(MakeAppend(temp.Path, "s1", "keep-me"));
        var tip = store.CurrentTipId;
        Assert.False(string.IsNullOrWhiteSpace(tip));

        var path = store.GetSessionFilePath(temp.Path);
        // Simulate crash mid-write: append a truncated JSON line.
        await File.AppendAllTextAsync(path, "{\"t\":\"turn\",\"sessionId\":\"s1\",\"user\":\"partial");

        var reloaded = CreateStore(temp.Path);
        var snap = await reloaded.TryLoadLatestAsync(temp.Path);
        Assert.NotNull(snap);

        var tree = await reloaded.GetTreeAsync(temp.Path);
        Assert.Contains(tree.Nodes, n => n.UserPreview.Contains("keep-me", StringComparison.Ordinal));
    }

    private static JsonlMafSessionStore CreateStore(string root)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new HeroOptions
        {
            WorkingDirectory = root,
            SessionDirectory = Path.Combine(root, "sessions"),
        });
        return new JsonlMafSessionStore(options, NullLogger<JsonlMafSessionStore>.Instance);
    }

    private static MafSessionAppendRequest MakeAppend(string cwd, string sessionId, string user) => new()
    {
        WorkingDirectory = cwd,
        SessionId = sessionId,
        UserInput = user,
        AssistantOutput = "ok",
        Session = JsonSerializer.SerializeToElement(new { user }),
    };

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "hero-tree-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
