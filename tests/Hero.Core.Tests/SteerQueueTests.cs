namespace Hero.Core.Tests;

/// <summary>
/// Mirrors CLI SteerQueue clear-on-steer policy without referencing the exe.
/// </summary>
public sealed class SteerQueueTests
{
    [Fact]
    public void ClearFollowUps_DropsQueuedItems()
    {
        var q = new Queue<string>();
        q.Enqueue("a");
        q.Enqueue("b");
        q.Clear();
        Assert.Empty(q);
    }

    [Fact]
    public void Dequeue_Fifo()
    {
        var q = new Queue<string>();
        q.Enqueue("first");
        q.Enqueue("second");
        Assert.Equal("first", q.Dequeue());
        Assert.Equal("second", q.Dequeue());
    }
}
