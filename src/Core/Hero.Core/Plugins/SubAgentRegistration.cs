using System;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Hero.Core.Plugins;

public sealed class SubAgentRegistration
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required Func<IChatClient, AIAgent> Factory { get; init; }
}
