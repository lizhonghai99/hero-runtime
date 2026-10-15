using System;
using System.Collections.Generic;
using System.Text;

namespace Hero.Cli;

/// <summary>
/// Mid-turn message queues (Pi-style): steer (change direction) and follow-up (after idle).
/// </summary>
internal sealed class SteerQueue
{
    private readonly object _gate = new();
    private readonly Queue<string> _steers = new();
    private readonly Queue<string> _followUps = new();

    public void EnqueueSteer(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        lock (_gate)
        {
            _steers.Enqueue(text.Trim());
        }
    }

    public void EnqueueFollowUp(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        lock (_gate)
        {
            _followUps.Enqueue(text.Trim());
        }
    }

    public void ClearFollowUps()
    {
        lock (_gate)
        {
            _followUps.Clear();
        }
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            _steers.Clear();
            _followUps.Clear();
        }
    }

    public bool TryDequeueSteer(out string text)
    {
        lock (_gate)
        {
            if (_steers.Count == 0)
            {
                text = string.Empty;
                return false;
            }

            text = _steers.Dequeue();
            return true;
        }
    }

    public bool TryDequeueFollowUp(out string text)
    {
        lock (_gate)
        {
            if (_followUps.Count == 0)
            {
                text = string.Empty;
                return false;
            }

            text = _followUps.Dequeue();
            return true;
        }
    }

    /// <summary>Drain all queued messages into editor-ready text (steers first).</summary>
    public string DrainToEditorText()
    {
        lock (_gate)
        {
            if (_steers.Count == 0 && _followUps.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            while (_steers.Count > 0)
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }

                sb.Append(_steers.Dequeue());
            }

            while (_followUps.Count > 0)
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }

                sb.Append(_followUps.Dequeue());
            }

            return sb.ToString();
        }
    }

    public int SteerCount
    {
        get
        {
            lock (_gate)
            {
                return _steers.Count;
            }
        }
    }

    public int FollowUpCount
    {
        get
        {
            lock (_gate)
            {
                return _followUps.Count;
            }
        }
    }

    public int TotalCount => SteerCount + FollowUpCount;
}
