using Hero.Core.Interaction;

namespace Hero.Core.Tests;

public sealed class SteerInputTests
{
    [Fact]
    public void Parse_PlainLine_IsSteer()
    {
        var item = SteerInput.Parse("do this instead");
        Assert.NotNull(item);
        Assert.Equal(SteerInput.Kind.Steer, item.Value.Kind);
        Assert.Equal("do this instead", item.Value.Text);
    }

    [Fact]
    public void Parse_PlusSpace_IsFollowUp()
    {
        var item = SteerInput.Parse("+ also check tests");
        Assert.NotNull(item);
        Assert.Equal(SteerInput.Kind.FollowUp, item.Value.Kind);
        Assert.Equal("also check tests", item.Value.Text);
    }

    [Fact]
    public void Parse_PlusNoSpace_IsFollowUp()
    {
        var item = SteerInput.Parse("+note");
        Assert.NotNull(item);
        Assert.Equal(SteerInput.Kind.FollowUp, item.Value.Kind);
        Assert.Equal("note", item.Value.Text);
    }

    [Fact]
    public void Parse_Empty_ReturnsNull()
    {
        Assert.Null(SteerInput.Parse(""));
        Assert.Null(SteerInput.Parse("   "));
        Assert.Null(SteerInput.Parse("+"));
        Assert.Null(SteerInput.Parse("+ "));
    }
}
