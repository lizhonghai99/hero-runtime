using System;
using System.IO;
using Hero.Core.Approval;
using Hero.Core.Convention;
using Hero.Core.Plugins;
using Hero.Core.Tools;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;

namespace Hero.Plugins.Coding;

/// <summary>
/// Local coding capabilities: read_file + run_shell + ExploreAgent sub-agent.
/// </summary>
public sealed class CodingPlugin : IPlugin
{
    public const string PluginId = "coding";
    public const string ExploreAgentName = "ExploreAgent";
    public const string MetadataShell = "shell";

    public string Id => PluginId;

    public void Configure(IPluginBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = builder.Options;
        var shellOverride = HostShellSelector.Select(options);
        var shellSelection = HostShellSelector.DescribeSelection(shellOverride);

        var shell = new LocalShellExecutor(new LocalShellExecutorOptions
        {
            Mode = ShellMode.Persistent,
            Timeout = LocalShellExecutor.DefaultTimeout,
            MaxOutputBytes = 64 * 1024,
            AcknowledgeUnsafe = true,
            WorkingDirectory = options.WorkingDirectory,
            ConfineWorkingDirectory = true,
            Shell = shellOverride,
        });

        var environment = new ShellEnvironmentProvider(
            shell,
            new ShellEnvironmentProviderOptions
            {
                // Skip docker: on Windows Desktop the probe often hangs on a named pipe
                // and blocks startup warmup for many seconds.
                ProbeTools = ["git", "dotnet", "node", "python"],
                ProbeTimeout = TimeSpan.FromSeconds(2),
            });

        var approvalMode = ToolApprovalModeParser.Parse(options.ToolApproval);
        var requireShellApproval = ToolApprovalModeParser.RequiresShellApproval(approvalMode);

        // Skills: put SKILL.md under {cwd}/.hero-agent/skills/<name>/ (or ~/.hero-agent/skills/).

        builder
            .AddDisposable(shell)
            .SetMetadata(MetadataShell, shellSelection)
            .SetMetadata("toolApproval", ToolApprovalModeParser.ToSettingsValue(approvalMode))
            .AddTool(ReadFileTool.Create(options.WorkingDirectory))
            .AddTool(shell.AsAIFunction(requireApproval: requireShellApproval))
            .AddContextProvider(environment)
            .AddWarmup(ct => environment.RefreshAsync(ct))
            .AddSubAgent(
                ExploreAgentName,
                "Reads text files under the working directory and returns a concise factual summary for the parent agent.",
                chatClient => CreateExploreAgent(chatClient, options.WorkingDirectory))
            .AppendInstructions(
                $"""
                Coding plugin tools:
                - read_file: read a text file under the working directory ({options.WorkingDirectory})
                - run_shell: run a shell command in that working directory
                - background_agents_* to delegate file investigation to {ExploreAgentName} and recover summaries

                Prefer tools over guessing. Match the shell family's idioms from the shell environment block.
                For multi-file exploration prefer {ExploreAgentName} (start → wait → get results → clear).
                """);
    }

    private static AIAgent CreateExploreAgent(IChatClient chatClient, string workingDirectory)
    {
        const string fallback =
            """
            You are ExploreAgent. Investigate by reading files with read_file.
            Return a short factual summary of what you found. Do not invent file contents.
            Do not run shell commands. Keep answers tight for the parent agent.
            """;

        var instructions = ConventionPromptStore.ResolveAgentInstructions(
            workingDirectory,
            ExploreAgentName,
            fallback);

        return chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = ExploreAgentName,
            Description =
                "Reads text files under the working directory and returns a concise factual summary for the parent agent.",
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = [ReadFileTool.Create(workingDirectory)],
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
            },
        });
    }
}
