# 归档源勘察与可行性（本机实测 2026-07-21）

> 编写模型: Claude Fable 5 (Cursor Agent) · 2026-07-21

原则：全部只读；数据库先复制再打开（或 `mode=ro&immutable=1`）；不解密；凭证文件显式跳过。

## 可行性总表

| 源 | 路径（Windows） | 格式 | 数据量（实测） | 评级 |
|---|---|---|---|---|
| **OpenCode** | `~\.local\share\opencode\opencode.db` | 明文 SQLite（session/message/part/project 等表） | **327 会话 / 9480 消息 / 44488 part**，库 1.09GB | 🟢 首选 |
| Codex | `~\.codex\sessions\YYYY\MM\DD\*.jsonl` | 明文 JSONL（信封 `{timestamp,type,payload}`） | 219MB | 🟢 |
| Claude Code | `~\.claude\projects\<slug>\*.jsonl` | 明文 JSONL（uuid/parentUuid 链） | 27MB | 🟢 |
| Cursor agent | `~\.cursor\chats\`（store.db protobuf blob） | protobuf，不宜直读 | — | 🟡 以 `--print --output-format stream-json` 捕获为主通道 |
| WorkBuddy memory | `~\.workbuddy\memory\*_memory.md` | 明文 md（Memory Block 四段 + 文末内嵌 RAW_JSON） | 单文件 ~7KB | 🟢 |
| WorkBuddy sessions | `~\.workbuddy\workbuddy.db` + `sessions\*.json` | 明文 SQLite（schema 完整）/ JSON | **表全 0 行**；sessions json 仅进程心跳 | 🟡 留 Adapter 占位，会话疑在云端 |
| Kimi memory vault | `%APPDATA%\kimi-desktop\daimon-share\daimon\agents\main\memory\vault\*.md` | 明文 md（about_user/index/log + sections.yaml） | 小 | 🟢偏🟡 |
| Kimi conversations | 同上 `sessions\hosted-logical\conversations.sqlite` | 明文 SQLite，schema 完整 | **0 行** | 🟡 占位 |
| Trae skills | `~\.trae-cn\skills\` + `builtin_skills\`（SKILL.md 标准布局） | 明文 md | 9 + 若干内置 | 🟡（并入统一技能库视图） |
| **Trae 会话库** | `%APPDATA%\TRAE SOLO CN\ModularData\ai-agent\database.db`（14.4MB）及 `Trae CN` 同名库（18.6MB） | **文件头随机字节 → SQLCipher 类加密** | — | 🔴 放弃内容归档，仅元数据 |
| Kimi/WorkBuddy 其它 | LevelDB / logs / local_storage gzip 块 | 二进制/日志 | — | 🔴 投入产出比低 |

其它注意：

- `kimi-desktop\bridge-store\token-store.json` 含凭证——Adapter **必须显式排除**。
- WorkBuddy memory.md 存在部分编码损伤字符，解析要容错。
- `~\.trae-cn\memory\user_profile.md` 为 0 字节；`work\`/`worktrees\` 是空壳。
- OpenCode Desktop（`%APPDATA%\ai.opencode.desktop`）只是 GUI 壳，数据与 CLI 同源，不需要单独 Adapter。

## 实现顺序

1. **OpenCode**：一个源覆盖本机 95%+ 可归档会话；`Microsoft.Data.Sqlite` 以 `Mode=ReadOnly` + 复制副本打开，不长持连接。
2. **WorkBuddy memory**：md + 内嵌 JSON，半小时工作量；顺手写好 `workbuddy.db` sessions 表读取（现在空，将来即插即用）。
3. **Kimi memory vault**：固定结构 md；同 Adapter 留 `conversations.sqlite` 读取路径。
4. **Trae skills**：与 Cursor/Codex 的 SKILL.md 合并成统一「技能库」只读视图。
5. Codex / Claude JSONL：随 Dispatch Adapter 的 Structured pane 需要一并实现（见 [02-dispatch-adapters.md](02-dispatch-adapters.md)）。

## 加密源的 UI 约定

Trae 两库在归档页显示为「存在 / 最近修改时间 / 大小 / 加密不可读」，提供「打开所在目录」按钮；不承诺、不尝试内容解析。
