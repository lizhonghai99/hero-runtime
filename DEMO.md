# Demo script (screen recording / live demo)

[中文](./DEMO-zh.md)

Goal: ~5–7 minutes showing the HERO loop — a **plugin-based Agent runtime on .NET** (interactive = MAF Harness, multi-step = MAF Workflows, capabilities via plugins, observable end-to-end). Main path: **tools → approval → Harness → export / resume**. Optional: `--json` / MCP / `/compact` / `/workflow`.

Prerequisites: `DEEPSEEK_API_KEY` set (optional `DEEPSEEK_END_POINT`). Start from the repo root.

```powershell
cd hero-runtime
# From source:
dotnet run --project src/Host/Hero.Cli -- --tool-approval shell
# Or after pack/install: hi --tool-approval shell
```

Startup banner should look like:

```text
hi — HERO 0.0.1
chat      DeepSeek · deepseek-chat
shell     ...
cwd       ...
plugins   observability, harness, coding, data-analysis, mcp, workflow
approval  shell
compact   auto
skills    project=…  plugin=…  user=…
turns     ...\turns.jsonl
```

Interaction notes:

- Busy + Enter = **steer** (cancel current turn and rerun with new text)
- **`Alt+Enter`** / `Ctrl+Q` = follow-up queued after the turn
- **`Esc`** = abort turn and restore queued text
- Session tree: `/tree` to list; `/tree <id>` to checkout and branch

One-shot print mode:

```powershell
dotnet run --project src/Host/Hero.Cli -- -p "Say hello in one sentence"
# Machine-readable:
dotnet run --project src/Host/Hero.Cli -- -p "Say hello in one sentence" --json
```

---

## Step 1: Tools + approval (Shell, MAF Harness HITL)

Approval uses Microsoft Harness: `ApprovalRequiredAIFunction` + `ToolApprovalAgent`. The CLI only shows the four-way prompt and maps choices to MAF `CreateResponse` / `CreateAlwaysApprove*`.

**You say:**

> Use the shell to count `.cs` files under the current directory. Reply with the number and one sentence on how you counted.

**Expect:**

- Yellow `approve?> run_shell(...)` with:
  - `[1] once  [2] always tool  [3] always args  [4] reject`
- After `1` (once): cyan `tool> run_shell(...)` and a result line
- Answer includes a count (with or without `bin`/`obj` is fine if the rule is clear)

Optional: on a second similar shell call, pick `2` to show session-level “always this tool” (Harness `ToolApprovalAgent` stores it on the session) with no further prompt.

(If you did not start with `--tool-approval shell`, there is no approval prompt — you go straight to `tool>`.  
Non-TTY / scripts: `--yes` or `--tool-approval auto` → Harness `AllToolsAutoApprovalRule`; default is fail-closed.)

---

## Step 2: Harness (plan → todos → ExploreAgent)

**You say:**

> Stay in plan mode: break “explain what this repo is for” into 3–4 todos. Do not execute yet.

**Expect:**

- `tool> todos_add(...)`
- A plan and wait for approval (Harness plan mode)

**You type:** `/todos`

**Expect:** list visible, items incomplete.

**You say:**

> Approve and switch to execute. First delegate ExploreAgent to read `Directory.Build.props` and `src/Host/Hero.Cli/Hero.Cli.csproj`, summarize key facts, then continue todos. Do not call `read_file` yourself.

**Or manually:** `/mode execute`, then send the delegation request above.

**Expect:**

- `background_agents_start_task` → `wait` → `get_task_results` → `clear`
- Summary mentions facts like `net10.0` / project names
- Todos progress via `todos_complete` (Loop may auto-continue unfinished items in execute)

**You type:** `/todos`

**Expect:** related items completed or clearly advanced.

---

## Step 3: Export session tree / HTML traces

**You type:** `/export`

**Expect:** two `export>` lines (`.json` and `.md`); Markdown shows tip and user previews.

**Then:** `/export --html`

**Expect:** one `export>` under `~/.hero-agent/exports/session-*.html`; open in a browser for the session tree and model-call groups labeled with agent names (`hi`, `ExploreAgent`, …).

Or after exit:

```powershell
dotnet run --project src/Host/Hero.Cli -- --continue --export
dotnet run --project src/Host/Hero.Cli -- --continue --export --html
```

---

## Step 4: Turn log + resume

**You type:** `/exit`

Open the `turns>` path from the banner; confirm JSONL lines with fields like `tools`, `durationMs`, `status`.

Restart in the same directory:

```powershell
dotnet run --project src/Host/Hero.Cli -- --continue
```

**You say:**

> In one sentence, recall what we were doing. Do not run tools again.

**Expect:** continuity from the resumed session tip.

Optional: while a turn is running, use steer (Enter) or follow-up (`Alt+Enter`) to show interrupt UX.

---

## Optional: Workflows (multi-step graph, not the REPL loop)

Prerequisite: project or user `.hero-agent/workflow.json` with `"enabled": true` (see `templates/workflow.json`).

**You type:** `/workflow` → should show `workflow: enabled…`

**Then:** `/workflow Summarize in three sentences what this repo does`

**Expect:** `workflow approve?>` (plan y/N); after approve, Plan → Research → Summarize and a `workflow> Summary: …` line.  
Narrative: the interactive loop stays Harness; this step demos a **MAF Workflows** predefined graph + HITL.

---

## Quick troubleshooting

| Symptom | Fix |
|------|------|
| `chat> not configured` | Ensure `DEEPSEEK_*` is set in this terminal |
| No `tool>` | Use a clearer “must use shell / must delegate ExploreAgent” prompt |
| No `approve?>` | Start with `--tool-approval shell`, or `/approval shell` then restart |
| ExploreAgent read fails | Paths are relative to `cwd`; prefer files that exist in the repo |
| DeepSeek + tools issues | Default `Reasoning=None`; try another `DEEPSEEK_MODEL` or check turn log `error` |
| `/workflow` shows idle | Write `.hero-agent/workflow.json`: `{ "enabled": true }` |
| Moonshot hangs with no output | Protocol `anthropic`, endpoint `…/anthropic` (see README providers) |
