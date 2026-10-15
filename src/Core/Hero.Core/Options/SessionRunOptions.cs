namespace Hero.Core.Options;

/// <summary>
/// Per-process session run flags (from CLI). Not bound from appsettings.
/// </summary>
public sealed class SessionRunOptions
{
    /// <summary>When true, resume the latest JSONL snapshot for the working directory.</summary>
    public bool Continue { get; set; }

    /// <summary>Resolved after startup (new or resumed).</summary>
    public string? ActiveSessionId { get; set; }

    /// <summary>Path of the JSONL file in use.</summary>
    public string? SessionFilePath { get; set; }

    public bool Resumed { get; set; }
}
