# 四家 CLI Adapter 契约（调度 + Transcript）

> 编写模型: Claude Fable 5 (Cursor Agent) · 2026-07-21

依据：2026-07-21 本机只读勘察（`C:\Users\yf`）+ 联网核对。本机版本参考：codex 0.145.0-alpha.18、claude 2.1.186。

## 1. Codex

### spawn/resume

| 场景 | 命令 |
|---|---|
| 新交互会话 | `codex` |
| 恢复最近（限 cwd） | `codex resume --last`（`--all` 跨目录） |
| 恢复指定 | `codex resume <session-id>` |
| 无头一次性 | `codex exec "<prompt>" --json`（NDJSON：`thread.*`/`item.*`） |
| 无头续跑 | `codex exec resume --last "<prompt>"` / `codex exec resume <id> "<prompt>"` |
| 有用旗标 | `--output-last-message <file>`；`--ephemeral` 不落盘 |

本机入口是 npm 包装：`~\Documents\Codex\.tools\codex-cli\codex.ps1` → `node .../@openai/codex/bin/codex.js`。**Adapter 直接 spawn `node codex.js <args>`**（绕过 pwsh，stdio 干净）。

### Transcript（实测）

路径：`~\.codex\sessions\<YYYY>\<MM>\<DD>\rollout-<时间>-<uuid>.jsonl`；文件名 uuid 即 session_id。

每行信封 `{"timestamp","type","payload"}`，`type`：

- `session_meta`（首行）：`payload.id`、`cwd`、`originator`（可区分 CLI / Codex Desktop / VS Code）、`cli_version`
- `turn_context`：每轮 cwd/approval/sandbox
- `response_item`：`payload.type` = `message`（role 有 `user/assistant/developer`，**developer 与注入型 user 行要过滤**）、`reasoning`（summary + encrypted_content）、`custom_tool_call`/`custom_tool_call_output`（按 `call_id` 配对；普通配置为 `function_call/*_output`）
- `event_msg`：**渲染首选**——`user_message`/`agent_message`/`agent_reasoning` 已是干净文本；`token_count` 有 usage
- `world_state`：环境快照，忽略

注意：`event_msg` 与 `response_item` 双读会重复，需去重；append-only 可增量 tail；末行可能是半行。

## 2. Claude Code

### spawn/resume（Windows 雷区）

| 场景 | 命令 | Windows 现状 |
|---|---|---|
| 新交互 | `claude` | ✅ |
| 交互恢复（选择器） | `claude --resume`（无 id） | ✅ |
| 恢复上一个 | `claude -c` | ❌ **冻结**（键盘无响应，只能杀进程） |
| 恢复指定 id | `claude --resume <id>` | ❌ 冻结 |
| 无头 | `claude -p "<prompt>" --output-format stream-json` | ✅ |
| 无头续跑 | `claude -p --resume <id> "<prompt>" --output-format stream-json` | ✅ **推荐** |

冻结 bug（#24394/#22964/#24191/#7455，截至 2026-02 未修）：Ink 渲染竞态，与插件无关。另 #22969：`claude "<prompt>"` 带参交互启动 stdin 冻结（按一次 Enter 可解）→ **交互 spawn 尽量裸启动**。

**Adapter 规则：交互恢复只用 `--resume` 选择器或会话内 `/resume`；程序化续跑一律 headless stream-json。**

### Transcript（实测）

路径：`~\.claude\projects\<路径编码 d--Code-...>\<sessionId>.jsonl`（编码：`\ : .` → `-`）；单文件可达 15MB+。

- `type:"user"`：文本或 `tool_result`（**工具结果也是 user 行**，按 `tool_use_id` 配对；顶层 `toolUseResult` 有结构化结果）
- assistant 行（顶层无 type，看 `message.role`）：`content[]` 含 `thinking`（正文常空）、`text`、`tool_use{id,name,input}`；外层 `model`、`usage`、`stop_reason`
- 链表：`uuid`/`parentUuid` 重建顺序；`isSidechain` 标记子代理支线
- 元数据行：`ai-title`（**会话标题**）、`queue-operation`、`attachment`、`file-history-snapshot`、`last-prompt`（渲染过滤）

## 3. OpenCode

### spawn/resume

| 场景 | 命令 |
|---|---|
| 新 TUI | `opencode`（`--prompt` `--agent` `-m provider/model`） |
| 续上一会话 | `opencode -c` |
| 续指定 | `opencode -s <sessionId>`（`--fork` 分叉） |
| 无头 | `opencode run "<msg>" --format json`；续跑加 `-c`/`-s` |
| 会话列表 | `opencode session list --format json` |
| 整会话导出 | `opencode export <sessionId>` |
| 直查库 | `opencode db "<SQL>"` |

### Transcript（实测 schema）

路径：`~\.local\share\opencode\opencode.db`（本机 ~1GB；注意不在 AppData）。

- `project`：`id`（git hash 或 `global`）、`worktree`
- `session`：`ses_` id、`parent_id`（**子代理会话挂父**）、`title`、`directory`、`model`(JSON)、`cost`、`tokens_*`、`time_*`
- `message`：`msg_` id + `data` JSON（role、agent、cost、tokens、time、finish）
- `part`：`data.type` 分布（实测 44488 条）：`tool`(14034)、`step-start/finish`、`reasoning`(8074)、`text`(4517)、`patch`(746)；**`tool` part 调用与结果同条**（`state.{status,input,output}`，免配对）

注意：运行中被锁 + WAL → **复制 db（连 -wal/-shm）到临时目录再读**，或 `mode=ro&immutable=1`，或干脆子进程 `opencode export`。首启有 10–30s sqlite 迁移，spawn 后等 `sqlite-migration:done`。

## 4. Cursor agent

### spawn/resume（本机未装；安装 `irm 'https://cursor.com/install?win32=true' | iex`）

| 场景 | 命令 |
|---|---|
| 新交互 | `agent` / `agent "<prompt>"` |
| 列表恢复 | `agent ls` |
| 恢复最近 | `agent resume` / `--continue` |
| 恢复指定 | `agent --resume="<chatId>"` |
| **预建会话拿 id** | `agent create-chat`（Adapter 很有用） |
| 无头 | `agent -p "<prompt>" --output-format stream-json [--stream-partial-output]`；续跑 `-p --resume <chatId>` |
| 自动批准/沙箱 | `-f`；`--sandbox enabled|disabled` |

### Transcript

- CLI：`~/.cursor/chats/<md5(cwd)>/<chatId>/store.db` —— `blobs` 表为 **protobuf**，社区一致结论：不适合旁路解析
- **主通道：捕获 stream-json 自建 transcript**。事件：`system/init`（含 session_id）、`user`、`assistant`、`tool_call`（`started|completed`，嵌套命名 `readToolCall/shellToolCall/...`）、`result`
- IDE 侧 `~/.cursor/projects/<slug>/agent-transcripts/*.jsonl` 可作只读补充，但 assistant 正文常见 `[REDACTED]`
- 交互模式无 TTY 会挂死 → 交互走 ConPTY 内嵌终端，程序化走 `-p`

## Canonical 字段对照

| Canonical | Codex | Claude | OpenCode | Cursor agent |
|---|---|---|---|---|
| Project.key | `session_meta.cwd` | 目录名+行内 `cwd` | `project.id`/`worktree` | `md5(cwd)`；`system.init.cwd` |
| Session.id | rollout uuid | jsonl 文件名 | `ses_…` | `chatId` |
| Session.title | 首条 user 截断 | `ai-title` 行 | `session.title` | `meta.name`/首 prompt |
| Session.parent | — | `isSidechain` | `parent_id` | `subagents\` 目录 |
| resume 令牌 | `codex resume <id>` | `-p --resume <id>`（TUI 禁） | `-s <id>` | `--resume <chatId>` |
| Message.text | `event_msg.*_message` | `content[].text` | part `text` | `content[].text` |
| Message.reasoning | `agent_reasoning` | `content[].thinking` | part `reasoning` | — |
| usage/cost | `token_count.info` | `message.usage` | `data.tokens/cost` | 仅 `result.duration_ms` |
| ToolCall.id | `call_id` | `toolu_…` | `callID` | 无（按序配对） |
| ToolCall.output | `*_output` 配对 | user 行 `tool_result` 配对 | 同条 part | `completed.result` |

## 结构性设计

1. **`TranscriptSource` 抽象**：`FileTail`（Codex/Claude）/ `SqliteSnapshot`（OpenCode）/ `CapturedStream`（Cursor）。各 Adapter 择一实现，canonical 层之上 UI 零特判。
2. **ToolCall 状态机归一**：`{ Id, Name, InputJson, OutputText, Status: pending→completed|error }`，配对逻辑收在 Adapter 内。
3. **交互 vs 程序化双通道**：每家 Adapter 同时声明 `interactiveSpec`（ConPTY 内嵌终端用）与 `headlessSpec`（stream-json 捕获用），Claude 的 interactive 禁 `-c`/`--resume <id>`。
