# 演示剧本（录屏 / 演示）

[English](./DEMO.md)

目标：约 5–7 分钟演示 HERO 闭环——**.NET 上插件化 Agent 运行时**（交互 = MAF Harness，多步 = MAF Workflows，能力靠插件，全程可观测）。主线：「工具 → 审批 → Harness → 导出/续聊」；可选：`--json` / MCP / `/compact` / `/workflow`。

前置：已设置 `DEEPSEEK_API_KEY`（可选 `DEEPSEEK_END_POINT`），在仓库根目录启动。

```powershell
cd hero-runtime
# 源码跑：
dotnet run --project src/Host/Hero.Cli -- --tool-approval shell
# 或已 pack 安装：hi --tool-approval shell
```

启动时应看到类似：

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

交互口径：

- 忙碌时 Enter = **steer**（取消当前轮并以新文本重跑）
- **`Alt+Enter`** / `Ctrl+Q` = follow-up 排队
- **`Esc`** = 中止当前轮并恢复队列文本
- 会话树：`/tree` 查看，`/tree <id>` checkout 后分支

脚本单轮：

```powershell
dotnet run --project src/Host/Hero.Cli -- -p "用一句话打个招呼"
# 机器可读：
dotnet run --project src/Host/Hero.Cli -- -p "用一句话打个招呼" --json
```

---

## 第一步：工具 + 审批（Shell，MAF Harness HITL）

审批走微软 Harness：`ApprovalRequiredAIFunction` + `ToolApprovalAgent`；CLI 只负责弹出四选项，并把选择映射为 MAF 的 `CreateResponse` / `CreateAlwaysApprove*`。

**你说：**

> 用 shell 统计当前目录下有多少个 `.cs` 文件，只给数字和一句口径说明。

**期望：**

- 出现黄色 `approve?> run_shell(...)` 与四选项提示：
  - `[1] once  [2] always tool  [3] always args  [4] reject`
- 输入 `1`（单次批准）后出现青色 `tool> run_shell(...)` 与结果行
- 回答里有数量（含/不含 `bin`/`obj` 均可，说清口径）

可选：第二次同类 shell 时选 `2`，展示「本会话工具级永久」（Harness `ToolApprovalAgent` 记在 session）不再弹窗。

（若未带 `--tool-approval shell`，则不会出现审批提示，直接 `tool>`。  
无 TTY / 脚本场景用 `--yes` 或 `--tool-approval auto` → Harness `AllToolsAutoApprovalRule`；默认 fail-closed。）

---

## 第二步：Harness（plan → todos → ExploreAgent）

**你说：**

> 先保持 plan 模式：把「给对方讲清这个仓库是干什么的」拆成 3～4 条 todo，先别 execute。

**期望：**

- `tool> todos_add(...)`
- 输出计划并等待批准（Harness plan 模式）

**你输入：** `/todos`

**期望：** 列表可见，状态未完成。

**你说：**

> 批准，请切到 execute。先委派 ExploreAgent 读 `Directory.Build.props` 和 `src/Host/Hero.Cli/Hero.Cli.csproj`，汇总关键点后再继续 todo。不要自己直接 read_file。

**或先手动：** `/mode execute`，再发上面委派要求。

**期望：**

- `background_agents_start_task` → `wait` → `get_task_results` → `clear`
- 摘要里出现 `net10.0` / 项目名等事实
- todo 逐步 `todos_complete`（execute 下 Loop 可能自动续推未完成项）

**你输入：** `/todos`

**期望：** 相关项已完成或明显推进。

---

## 第三步：导出会话树 / HTML 轨迹

**你输入：** `/export`

**期望：** 打印两行 `export>`，分别是 `.json` 与 `.md`；打开 Markdown 能看到 tip 与用户预览。

**再输入：** `/export --html`

**期望：** 一行 `export>` 指向 `~/.hero-agent/exports/session-*.html`；浏览器打开可见会话树，以及带 `hi` / `ExploreAgent` 等 agent 名的 model call 分组。

也可退出后：

```powershell
dotnet run --project src/Host/Hero.Cli -- --continue --export
dotnet run --project src/Host/Hero.Cli -- --continue --export --html
```

---

## 第四步：Turn 日志 + 续聊

**你输入：** `/exit`

打开启动时打印的 `turns>` 文件，确认有 JSON 行，字段含 `tools`、`durationMs`、`status`。

同目录再开：

```powershell
dotnet run --project src/Host/Hero.Cli -- --continue
```

**你说：**

> 用一句话回顾我们刚才在干什么，不要重新跑工具。

**期望：** 能接上上一会话上下文（session 已 resume）。

可选：运行中用 steer（Enter）或 follow-up（`Alt+Enter`）展示中断 UX。

---

## 可选：Workflows（多步图，非 REPL 循环）

前置：项目或用户目录 `.hero-agent/workflow.json` 含 `"enabled": true`（见 `templates/workflow.json`）。

**你输入：** `/workflow` → 应显示 `workflow: enabled…`

**再输入：** `/workflow 用三句话说明本仓库是做什么的`

**期望：** 先弹出 `workflow approve?>`（计划 y/N）；同意后跑 Plan → Research → Summarize，打印 `workflow> Summary: …`。  
说明口径：交互主循环仍是 Harness；这里演示的是 **MAF Workflows** 预定义图 + HITL。

---

## 失败时快速排查

| 现象 | 处理 |
|------|------|
| `chat> not configured` | 检查 `DEEPSEEK_*` 是否在当前终端生效 |
| 无 `tool>` | 换更明确的「必须用 shell / 必须委派 ExploreAgent」措辞 |
| 无 `approve?>` | 确认以 `--tool-approval shell` 启动，或 `/approval shell` 后重启 |
| ExploreAgent 读失败 | 路径相对 `cwd`；优先用仓库内已有文件 |
| DeepSeek + tools 异常 | 已默认 `Reasoning=None`；仍失败则换 `DEEPSEEK_MODEL` 或看 turn 日志 `error` |
| `/workflow` 显示 idle | 写入 `.hero-agent/workflow.json`：`{ "enabled": true }` |
| Moonshot 卡住无输出 | 确认协议为 `anthropic`、端点为 `…/anthropic`（见 README 提供商表） |
