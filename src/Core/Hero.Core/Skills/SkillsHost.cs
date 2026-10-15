using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hero.Core.Convention;
using Hero.Core.Options;
using Hero.Core.Plugins;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hero.Core.Skills;

/// <summary>
/// Resolves convention + plugin skill roots for Harness.
/// Discovery order (first wins on name collision via <see cref="DeduplicatingAgentSkillsSource"/>):
/// project → plugin <c>AddSkillsRoot</c> → user global.
/// The actual provider is MAF Harness built-in <c>AgentSkillsProvider</c> + <see cref="AgentSkillsSource"/>.
/// </summary>
public sealed class SkillsHost
{
    private readonly ILoggerFactory? _loggerFactory;

    public SkillsHost(
        IOptions<HeroOptions> heroOptions,
        PluginCatalog catalog,
        ILogger<SkillsHost> logger,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(heroOptions);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);

        _loggerFactory = loggerFactory;

        var cwd = heroOptions.Value.WorkingDirectory;
        var projectRoot = ConventionPaths.ProjectSkillsRoot(cwd);
        var userRoot = ConventionPaths.UserSkillsRoot;

        var roots = new List<string>();
        if (Directory.Exists(projectRoot))
        {
            roots.Add(projectRoot);
        }

        foreach (var pluginRoot in catalog.SkillsRoots)
        {
            if (Directory.Exists(pluginRoot)
                && !roots.Contains(pluginRoot, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(pluginRoot);
            }
        }

        if (Directory.Exists(userRoot)
            && !roots.Contains(userRoot, StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(userRoot);
        }

        Roots = roots;
        ProjectSkillCount = CountSkillMarkdownFiles(projectRoot);
        PluginSkillCount = catalog.SkillsRoots.Sum(CountSkillMarkdownFiles);
        UserSkillCount = CountSkillMarkdownFiles(userRoot);

        if (roots.Count == 0)
        {
            logger.LogDebug(
                "No skills directories found (project={Project}, user={User}, pluginRoots={PluginRoots})",
                projectRoot,
                userRoot,
                catalog.SkillsRoots.Count);
            return;
        }

        logger.LogDebug(
            "Skills roots ready for Harness AgentSkillsSource: project={ProjectCount}, plugin={PluginCount}, user={UserCount}, roots={RootCount}",
            ProjectSkillCount,
            PluginSkillCount,
            UserSkillCount,
            roots.Count);
    }

    public IReadOnlyList<string> Roots { get; }

    public int ProjectSkillCount { get; }

    public int PluginSkillCount { get; }

    public int UserSkillCount { get; }

    public bool HasSkills => Roots.Count > 0;

    /// <summary>
    /// MAF file-based source for <see cref="HarnessAgentOptions.AgentSkillsSource"/>.
    /// Null when no convention/plugin skill directories exist (keeps Harness skills disabled).
    /// Bodies are loaded on demand via Harness <c>load_skill</c>, not injected into the system prompt.
    /// </summary>
    public AgentSkillsSource? CreateSkillsSource()
    {
        if (Roots.Count == 0)
        {
            return null;
        }

        AgentSkillsSource fileSource = new AgentFileSkillsSource(
            Roots,
            scriptRunner: null,
            options: null,
            loggerFactory: _loggerFactory);

        return new DeduplicatingAgentSkillsSource(fileSource, _loggerFactory);
    }

    /// <summary>Skill folder names only (catalog) — does not read SKILL.md bodies.</summary>
    public IReadOnlyList<string> ListSkillNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    if (File.Exists(Path.Combine(dir, "SKILL.md")))
                    {
                        names.Add(Path.GetFileName(dir));
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return names.ToList();
    }

    private static int CountSkillMarkdownFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        try
        {
            return Directory.EnumerateDirectories(root)
                .Count(dir => File.Exists(Path.Combine(dir, "SKILL.md")));
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
