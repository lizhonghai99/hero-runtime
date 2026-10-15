using System;
using System.IO;
using System.Text;
using Hero.Core.Options;

namespace Hero.Core.Convention;

/// <summary>
/// Loads prompt text from convention paths.
/// Priority: project <c>.hero-agent/</c> → user <c>~/.hero-agent/</c> → built-in / config fallback.
/// </summary>
public static class ConventionPromptStore
{
    /// <summary>
    /// Main agent personality. File wins over <see cref="HeroOptions.Instructions"/>.
    /// </summary>
    public static string ResolveSystemInstructions(HeroOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var fromFile = ReadFirstExisting(
            ConventionPaths.ProjectSystemPromptPath(options.WorkingDirectory),
            ConventionPaths.UserSystemPromptPath);

        return string.IsNullOrWhiteSpace(fromFile)
            ? options.Instructions
            : fromFile.Trim();
    }

    /// <summary>
    /// Optional project <c>AGENTS.md</c> (cwd) appended after system instructions.
    /// </summary>
    public static string? TryLoadProjectAgentsMd(string workingDirectory)
    {
        return ReadIfExists(ConventionPaths.ProjectAgentsMdPath(workingDirectory));
    }

    /// <summary>
    /// Sub-agent system prompt. Returns <paramref name="fallback"/> when no file exists.
    /// </summary>
    public static string ResolveAgentInstructions(string workingDirectory, string agentName, string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);

        var fromFile = ReadFirstExisting(
            ConventionPaths.ProjectAgentPromptPath(workingDirectory, agentName),
            ConventionPaths.UserAgentPromptPath(agentName));

        return string.IsNullOrWhiteSpace(fromFile) ? fallback : fromFile.Trim();
    }

    /// <summary>
    /// Which SYSTEM.md path won (for CLI banner), or null if using config/built-in.
    /// </summary>
    public static string? ResolveSystemPromptSource(HeroOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var project = ConventionPaths.ProjectSystemPromptPath(options.WorkingDirectory);
        if (File.Exists(project))
        {
            return project;
        }

        var user = ConventionPaths.UserSystemPromptPath;
        return File.Exists(user) ? user : null;
    }

    public static string? ResolveAgentPromptSource(string workingDirectory, string agentName)
    {
        var project = ConventionPaths.ProjectAgentPromptPath(workingDirectory, agentName);
        if (File.Exists(project))
        {
            return project;
        }

        var user = ConventionPaths.UserAgentPromptPath(agentName);
        return File.Exists(user) ? user : null;
    }

    private static string? ReadFirstExisting(params string[] paths)
    {
        foreach (var path in paths)
        {
            var text = ReadIfExists(path);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? ReadIfExists(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path, Encoding.UTF8);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
