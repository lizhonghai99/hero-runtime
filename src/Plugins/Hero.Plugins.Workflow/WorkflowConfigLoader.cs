using System;
using System.IO;
using System.Text.Json;
using Hero.Core.Convention;

namespace Hero.Plugins.Workflow;

/// <summary>Loads and merges project + user <c>workflow.json</c> (project wins).</summary>
public static class WorkflowConfigLoader
{
    public const string FileName = "workflow.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string ProjectConfigPath(string workingDirectory) =>
        Path.Combine(ConventionPaths.ProjectRoot(workingDirectory), FileName);

    public static string UserConfigPath =>
        Path.Combine(ConventionPaths.UserRoot, FileName);

    public static WorkflowConfigDocument LoadMerged(string workingDirectory, string? userConfigPath = null)
    {
        var merged = new WorkflowConfigDocument();
        TryMergeFile(userConfigPath ?? UserConfigPath, merged);
        TryMergeFile(ProjectConfigPath(workingDirectory), merged);
        return merged;
    }

    public static bool IsEnabled(WorkflowConfigDocument doc) =>
        doc.Enabled == true;

    public static WorkflowConfigDocument? TryLoadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new WorkflowConfigDocument();
            }

            return JsonSerializer.Deserialize<WorkflowConfigDocument>(json, JsonOptions)
                   ?? new WorkflowConfigDocument();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void TryMergeFile(string path, WorkflowConfigDocument target)
    {
        var doc = TryLoadFile(path);
        if (doc is null)
        {
            return;
        }

        if (doc.Enabled.HasValue)
        {
            target.Enabled = doc.Enabled;
        }

        if (doc.Checkpoint.HasValue)
        {
            target.Checkpoint = doc.Checkpoint;
        }

        if (doc.TimeoutSeconds.HasValue)
        {
            target.TimeoutSeconds = doc.TimeoutSeconds;
        }

        if (doc.AutoApprove.HasValue)
        {
            target.AutoApprove = doc.AutoApprove;
        }
    }
}
