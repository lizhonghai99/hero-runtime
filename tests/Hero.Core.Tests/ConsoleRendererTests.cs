using Hero.Cli;

namespace Hero.Core.Tests;

public sealed class ConsoleRendererTests
{
    public ConsoleRendererTests()
    {
        ConsoleRenderer.ResetForTests();
    }

    [Fact]
    public void Detect_JsonMode_DisablesStdoutRich()
    {
        var caps = ConsoleRenderer.Detect(jsonMode: true, stdoutRedirected: false, stderrRedirected: false);
        Assert.True(caps.JsonMode);
        Assert.False(caps.StdoutRich);
        Assert.True(caps.StderrRich);
    }

    [Fact]
    public void Detect_RedirectedStdout_DisablesRich()
    {
        var caps = ConsoleRenderer.Detect(jsonMode: false, stdoutRedirected: true, stderrRedirected: false);
        Assert.False(caps.StdoutRich);
        Assert.True(caps.StderrRich);
    }

    [Fact]
    public void Detect_Interactive_EnablesRich()
    {
        var caps = ConsoleRenderer.Detect(jsonMode: false, stdoutRedirected: false, stderrRedirected: false);
        Assert.True(caps.StdoutRich);
        Assert.True(caps.StderrRich);
    }

    [Fact]
    public void WriteTable_WhenRedirected_WritesPlainTextWithoutAnsi()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var renderer = ConsoleRenderer.Initialize(
            jsonMode: false,
            stdoutRedirected: true,
            stderrRedirected: true,
            stdoutWriter: stdout,
            stderrWriter: stderr);

        renderer.WriteTable(
            ["server", "state"],
            [["fs", "connected"]]);

        var text = stdout.ToString();
        Assert.Contains("server", text);
        Assert.Contains("fs", text);
        Assert.DoesNotContain("\u001b[", text); // no ANSI CSI
        Assert.DoesNotContain("[cyan]", text);
    }

    [Fact]
    public void WriteLine_WhenJsonMode_DoesNotTouchStdout()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var renderer = ConsoleRenderer.Initialize(
            jsonMode: true,
            stdoutRedirected: false,
            stderrRedirected: true,
            stdoutWriter: stdout,
            stderrWriter: stderr);

        renderer.WriteLine("hello");
        renderer.WriteTable(["a"], [["b"]]);

        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public void WriteTool_GoesToStderr_PlainWhenRedirected()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var renderer = ConsoleRenderer.Initialize(
            jsonMode: false,
            stdoutRedirected: true,
            stderrRedirected: true,
            stdoutWriter: stdout,
            stderrWriter: stderr);

        renderer.WriteTool("run_shell");

        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Contains("tool> run_shell", stderr.ToString());
        Assert.DoesNotContain("\u001b[", stderr.ToString());
    }
}
