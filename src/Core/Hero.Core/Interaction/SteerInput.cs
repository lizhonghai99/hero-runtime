using System;

namespace Hero.Core.Interaction;

/// <summary>
/// Classifies a stdin line while a turn is running (Pi-style).
/// <see cref="Kind.Steer"/> = change direction (host cancels turn and reruns);
/// <see cref="Kind.FollowUp"/> = queue until the turn finishes.
/// </summary>
public static class SteerInput
{
    public enum Kind
    {
        /// <summary>Change direction: cancel in-flight turn, then run as the next user turn.</summary>
        Steer,
        FollowUp,
    }

    public readonly record struct Item(Kind Kind, string Text);

    /// <summary>
    /// Follow-up if the line starts with <c>+</c> (or was submitted via Alt+Enter with a <c>+</c> prefix).
    /// Otherwise guidance (steer).
    /// </summary>
    public static Item? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith("+ ", StringComparison.Ordinal))
        {
            var body = trimmed[2..].Trim();
            return string.IsNullOrEmpty(body) ? null : new Item(Kind.FollowUp, body);
        }

        if (trimmed.Equals("+", StringComparison.Ordinal))
        {
            return null;
        }

        if (trimmed.StartsWith('+') && trimmed.Length > 1 && !char.IsWhiteSpace(trimmed[1]))
        {
            var body = trimmed[1..].Trim();
            return string.IsNullOrEmpty(body) ? null : new Item(Kind.FollowUp, body);
        }

        return new Item(Kind.Steer, trimmed);
    }
}
