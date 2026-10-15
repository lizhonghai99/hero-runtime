# Project notes for hi

Prefer running `hi` from this directory (repo / project root), not from `bin/Debug`.

## Architecture (short)

- **Plugin ≠ Agent.** `IPlugin` packs capability into the one interactive **HarnessAgent**.
- **Capability ≠ only Tools.** Plugins may add tools, slash commands, context, chat decorators, skills roots, and optionally **sub-agents** (`AddSubAgent` → BackgroundAgents).
- **Sub-agent when:** different persona + tool subset + own multi-step loop (samples: ExploreAgent, DataAnalyst). Otherwise mount a tool.
- **Orchestration:** interactive turns = Harness loop; predefined multi-step = **MAF Workflows** (`/workflow` / `run_workflow` when `.hero-agent/workflow.json` has `"enabled": true`).

See README.md § «Plugins ≠ Agents» and § «Orchestration (Workflows)» for the full tables.
