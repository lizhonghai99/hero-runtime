using System;
using System.IO;
using Hero.Core.Session;

namespace Hero.Core.Convention;

/// <summary>
/// Convention-over-configuration roots. End users edit files here — no compile required.
/// </summary>
public static class ConventionPaths
{
    public const string RootFolderName = ".hero-agent";

    public const string SystemFileName = "SYSTEM.md";

    public const string AgentsFolderName = "agents";

    public const string SkillsFolderName = "skills";

    public const string MemoryFolderName = "memory";

    public const string ProjectAgentsFileName = "AGENTS.md";

    public static string UserRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            RootFolderName);

    public static string ProjectRoot(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        return Path.Combine(Path.GetFullPath(workingDirectory), RootFolderName);
    }

    public static string UserSystemPromptPath => Path.Combine(UserRoot, SystemFileName);

    public static string ProjectSystemPromptPath(string workingDirectory) =>
        Path.Combine(ProjectRoot(workingDirectory), SystemFileName);

    public static string UserAgentPromptPath(string agentName) =>
        Path.Combine(UserRoot, AgentsFolderName, agentName + ".md");

    public static string ProjectAgentPromptPath(string workingDirectory, string agentName) =>
        Path.Combine(ProjectRoot(workingDirectory), AgentsFolderName, agentName + ".md");

    public static string ProjectAgentsMdPath(string workingDirectory) =>
        Path.Combine(Path.GetFullPath(workingDirectory), ProjectAgentsFileName);

    public static string UserSkillsRoot => Path.Combine(UserRoot, SkillsFolderName);

    public static string ProjectSkillsRoot(string workingDirectory) =>
        Path.Combine(ProjectRoot(workingDirectory), SkillsFolderName);

    /// <summary>
    /// Long-term file memory for a working directory:
    /// <c>~/.hero-agent/memory/&lt;cwd-key&gt;/</c> (sibling of sessions/ and logs/).
    /// </summary>
    public static string UserMemoryRoot(string workingDirectory)
    {
        var key = SessionPath.GetDirectoryKey(workingDirectory);
        return Path.Combine(UserRoot, MemoryFolderName, key);
    }
}
