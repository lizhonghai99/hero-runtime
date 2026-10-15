using System;
using Hero.Core.Convention;
using Hero.Core.Plugins;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Hero.Plugins.DataAnalysis;

/// <summary>
/// Demo plugin: CSV-oriented tools + DataAnalyst sub-agent.
/// </summary>
public sealed class DataAnalysisPlugin : IPlugin
{
    public const string PluginId = "data-analysis";
    public const string DataAnalystName = "DataAnalyst";

    public string Id => PluginId;

    public void Configure(IPluginBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var cwd = builder.Options.WorkingDirectory;
        var tools = CsvTools.CreateAll(cwd);

        foreach (var tool in tools)
        {
            builder.AddTool(tool);
        }

        builder
            .AddSubAgent(
                DataAnalystName,
                "Analyzes CSV/TSV data files under the working directory and returns concise statistical findings.",
                chatClient => CreateDataAnalyst(chatClient, cwd, tools))
            .AppendInstructions(
                $"""
                Data-analysis plugin tools:
                - list_data_files: find .csv/.tsv/.json under the working directory
                - summarize_csv: column/row summary + numeric min/max/avg
                - peek_csv: preview first rows
                - background_agents_* may delegate deep CSV work to {DataAnalystName}

                For tabular questions prefer these tools (or {DataAnalystName}) over guessing numbers.
                """);
    }

    private static AIAgent CreateDataAnalyst(IChatClient chatClient, string workingDirectory, IReadOnlyList<AITool> tools)
    {
        const string fallback =
            """
            You are DataAnalyst. Use list_data_files, summarize_csv, and peek_csv only.
            Report factual findings with numbers from tool results. Do not invent statistics.
            Keep answers short and structured for the parent agent.
            """;

        var instructions = ConventionPromptStore.ResolveAgentInstructions(
            workingDirectory,
            DataAnalystName,
            fallback);

        return chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = DataAnalystName,
            Description =
                "Analyzes CSV/TSV data files under the working directory and returns concise statistical findings.",
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = [.. tools],
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
            },
        });
    }
}
