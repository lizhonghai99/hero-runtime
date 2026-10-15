using System;
using System.IO;

namespace Hero.Core.Options;

/// <summary>
/// Agent product options. Bound from configuration section <c>Hero</c>.
/// </summary>
public sealed class HeroOptions
{
    public const string SectionName = "Hero";

    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;

    public string SessionDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hero-agent", "sessions");

    public string TurnLogDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hero-agent", "logs");

    public string Instructions { get; set; } =
        """
        You are hi, a concise coding agent for the Hero (.NET / MAF) runtime.
        Reply in the same language the user is using.
        Prefer short, direct answers. No capability menus unless asked.
        Prefer tools over guessing. Do not invent file contents.
        Casual chat stays casual — do not announce plan mode or list Explore/Run/Analyze options.
        """;

    public string AgentName { get; set; } = "hi";

    /// <summary>
    /// Optional shell override for MAF LocalShellExecutor: pwsh, powershell, cmd, bash, sh, wsl.
    /// Empty = auto (Windows pwsh→powershell, else cmd if no PowerShell; POSIX bash→sh).
    /// Also overridable via env HERO_SHELL / AGENT_FRAMEWORK_SHELL.
    /// </summary>
    public string? Shell { get; set; }

    /// <summary>Model context window for status-bar occupancy and compaction budget.</summary>
    public int MaxContextWindowTokens { get; set; } = 128_000;

    /// <summary>Per-call max output tokens (also reserved inside the context window budget).</summary>
    public int MaxOutputTokens { get; set; } = 8_192;

    /// <summary>Status-bar label for reasoning effort (must match ChatOptions.Reasoning).</summary>
    public string ReasoningLevel { get; set; } = "none";

    /// <summary>
    /// Compaction mode: <c>auto</c> = MAF ContextWindowCompactionStrategy (tool eviction + truncation);
    /// <c>none</c> = disabled. Status bar shows this label.
    /// </summary>
    public string CompactionMode { get; set; } = "auto";

    /// <summary>
    /// Tool approval: <c>off</c> | <c>shell</c> | <c>all</c> | <c>auto</c>. Default <c>shell</c>
    /// (run_shell requires HITL). <c>off</c> is an explicit demo/unsafe opt-out.
    /// <c>auto</c> = wrap tools like <c>all</c> and auto-approve (explicit automation opt-in).
    /// </summary>
    public string ToolApproval { get; set; } = "shell";

    /// <summary>Fraction of input budget that triggers tool-result eviction (MAF default 0.5).</summary>
    public double CompactionToolEvictionThreshold { get; set; } = 0.5;

    /// <summary>Fraction of input budget that triggers truncation (MAF default 0.8).</summary>
    public double CompactionTruncationThreshold { get; set; } = 0.8;

    /// <summary>USD per 1M input tokens for status-bar cost (DeepSeek-chat-ish defaults).</summary>
    public double? InputUsdPer1M { get; set; } = 0.27;

    /// <summary>USD per 1M output tokens.</summary>
    public double? OutputUsdPer1M { get; set; } = 1.10;

    /// <summary>USD per 1M cached input tokens.</summary>
    public double? CachedInputUsdPer1M { get; set; } = 0.07;

    public static HeroOptions CreateDefault() => new();
}

