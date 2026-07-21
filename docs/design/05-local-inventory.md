# 本机 AI 目录盘点（C:\Users\yf，2026-07-21 只读扫描）

> 编写模型: Claude Fable 5 (Cursor Agent) · 2026-07-21

Adapter `discoverRoots()` 的基准数据；路径以本表为准，实施时再实时探测。

## 有数据

| 路径 | 约大小 | 用途 | vibe-hub 角色 |
|---|---|---|---|
| `~\AppData\Roaming\Cursor` | 9.4 GB | Cursor IDE 状态/缓存 | 不整盘导入；只关心未来 agent CLI 会话 |
| `~\.codex` | 3.8 GB | Codex sessions + 配置 + skills(17) + `AGENTS.md`(手写 8.7KB) | Dispatch + Archive + Inject 落点 |
| `~\AppData\Roaming\TRAE SOLO CN` | 3.8 GB | Trae Solo（`ModularData\ai-agent\database.db` 14.4MB **加密**） | 元数据归档 only |
| `~\.workbuddy` | 1.3 GB | WorkBuddy（memory.md 明文；workbuddy.db 明文但 0 行；logs 巨大） | Archive（memory 先行） |
| `~\.local\share\opencode` | 1.1 GB | **opencode.db 1.09GB 明文：327 会话/9480 消息** | Archive 首源 + Dispatch |
| `~\AppData\Roaming\kimi-desktop` | 1.0 GB | Kimi 桌面（memory vault md 明文；conversations.sqlite 0 行；token-store.json **凭证勿碰**） | Archive（vault） |
| `~\.trae-cn` | 594 MB | Trae CN 用户态（skills 9 个明文；memory 0 字节；work/worktrees 空壳） | Skills 库视图 |
| `~\AppData\Roaming\Codex` | 336 MB | Codex 桌面/App | 暂不处理 |
| `~\AppData\Roaming\Trae CN` | 324 MB | Trae CN IDE（database.db 18.6MB **加密**） | 元数据 only |
| `~\AppData\Roaming\ai.opencode.desktop` | 185 MB | OpenCode Desktop GUI（数据与 CLI 同源） | 无需独立 Adapter |
| `~\.kimi-work` | 141 MB | Kimi Work 本地 bin（kimi-tools/kimi-slides） | 无会话数据 |
| `~\.cursor` | 114 MB | Cursor agent 侧（projects/chats；chats 为 protobuf store.db） | Dispatch（装 agent 后）+ stream-json 捕获 |
| `~\.claude` | 80 MB | Claude projects JSONL + settings.json | Dispatch + Archive |
| `~\.config\opencode` | 53 MB | OpenCode 配置（**opencode.jsonc 带注释**、插件） | Inject/MCP 落点 |
| `~\.kimi-webbridge` | 10 MB | WebBridge daemon（pid/identity） | 无 |
| `~\AppData\Local\WorkBuddy` | 6 MB | WorkBuddy 本地 | 无 |

## 未找到 / 未安装

- 目录：`~\.kimi-code`、`~\.pi`、`~\.omp`、`~\.gemini`、`~\.agents`、`%APPDATA%\Trae`（国际版）、`TRAE SOLO`（非 CN）
- PATH 中 CLI：有 `opencode`（npm）、`codex`（`~\Documents\Codex\.tools\codex-cli\codex.ps1`）、`claude`（npm）、`cursor`（编辑器 cmd）；**无 `agent`、`pi`、`kimi`**

## 待办关联

- 安装 Cursor agent CLI：`irm 'https://cursor.com/install?win32=true' | iex`（PENDING P5）
- 大库（opencode.db 1GB、Cursor Roaming 9GB）扫描要限流：只读副本 + 增量 mtime 过滤
