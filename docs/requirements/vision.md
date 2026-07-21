# vibe-hub 产品定位与需求（vision）

> 编写模型: Claude Fable 5 (Cursor Agent) · 2026-07-21

## 一句话

自用的 Windows 桌面「AI coding agent 工作台」：**项目/工作区由 vibe-hub 负责（真源），分发给各 CLI 与工具只是临时租约**；一个 WPF 窗口内完成 调度、真归档、迁移、注入、Skills 管理与会话蒸馏。

## 核心理念：所有权反转

- Hub 是 system of record：项目、任务、记忆、归档、技能启用集都归 Hub 所有
- 各 CLI/工具侧的状态（AGENTS.md 管理块、skills 目录、会话文件）都是 Hub 的**投影或临时缓存**，可随时重建
- 会话生命周期：`Draft(备上下文) → Leased(租给 CLI 跑) → Harvested(收割进 vault) → Distilled(蒸馏) → Archived(冷存)`

## 背景与动机

本机同时使用 OpenCode / Codex / Claude Code / Cursor（未来 agent CLI），以及封闭桌面 Work 产品（Kimi Work、腾讯 WorkBuddy、Trae CN / TRAE SOLO CN）。痛点：

1. 会话散落在十几个目录/数据库里，无统一检索与归档；
2. 多个 CLI 并行开工时缺一个统一的启停/续聊入口；
3. 跨工具的 memory / handoff / rules 靠手工复制；
4. Skills 各家一套目录，下载与启停无统一管理。

2026 年开源生态调研结论：调度（AgentMux/Hive）、归档（ChatMem/CodeSesh）、Skills（Skiller/Skills Hub）、Memory（Memstem/ChatMem）各有成熟单品，但 **没有一款同时满足「Windows + .NET 桌面 + 内嵌终端 + 覆盖本机这批源」**，故自建薄壳拼装。

## 硬约束（不可协商）

| # | 约束 |
|---|---|
| C1 | `.NET 10`（`net10.0-windows`），桌面 WPF GUI 主窗口应用 |
| C2 | 终端控件**内嵌**主窗口布局（Tab/Split 均可）；外置 `wt.exe`/独立 console 不得作为主路径 |
| C3 | 调度仅针对开放 CLI；封闭 Work App 只读归档，不注入进程、不破解加密库 |
| C4 | 重拼装、小改造、自用优先；能依赖现成开源就不重写 |
| C5 | 单元测试不得启动真实 GUI/ConPTY/系统进程（mock `IProcessLauncher` / `IPseudoTerminal`） |

## 功能需求（按优先级）

### F1 调度（Dispatch）

- 以子进程 + ConPTY 启动/续聊一等 CLI：OpenCode、Codex、Claude Code、Cursor agent
- Supervisor 状态机：`Idle → Spawning → Running → (Exited|Failed)`；命令 `start | resume | kill | focus`；并发上限
- 每个 job 记录：provider、cwd、pid、sessionId、时间戳、退出码

### F2 内嵌终端（双轨）

- **Terminal pane**：ConPTY 原始字节 → 内嵌终端控件，完整 TUI（alt-screen、颜色、键盘、鼠标、中文 IME）
- **Structured pane**：**旁路读磁盘 transcript/DB**（不剥 ANSI 当主路径），渲染消息气泡 + 工具调用折叠；同一 job 下与 Terminal Tab 切换
- Structured pane 输入可选写回 PTY；审批/picker 类交互回 Terminal

### F3 真归档（Archive = ingest 进 vault，非只读镜像）

- 统一 canonical 模型：`Project → Session → Message → ToolCall`
- **Ingest**：会话复制进 Hub 自己的 vault（raw 保真副本 + canonical 解析双层），工具卸载/清数据/换机后归档仍在；解析成功且消息数/哈希核对后才标记 Harvested
- 第一批源（按本机勘察可行性排序）：OpenCode db（327 会话/9480 消息，明文）→ Codex/Claude JSONL → Cursor 捕获流 → WorkBuddy memory.md → Kimi memory vault → Trae skills 库
- 加密源（Trae 两个 `database.db`）只展示元数据（存在/mtime/大小），UI 不承诺内容归档
- 工具侧清理（可选，默认关）：仅清已核实 ingest 的文件型会话；加密源与共享 db 永不动

### F3b 会话迁移

- 同工具续跑：原生 resume 令牌
- 同工具跨机：搬 vault + 原生数据文件（Codex/Claude jsonl 放回原路径可续 resume）
- **跨工具：语义迁移**——蒸馏出 handoff/context → 注入投影 → 新工具开新会话（各家 transcript 互不兼容，不承诺 raw 移植）

### F4 注入（Inject）

- Hub 权威目录（sink）：`<repo>/inject/<projectId>/{memory,handoff,context}.md`
- 投影到各家自动加载路径：**管理块合并**（`<!-- vibe-hub:begin/end -->`），绝不整文件覆盖；Claude 可用 `@path` import 简化
- `inject.toggle` 可断开投影而不删 sink

### F5 Skills（下载 + 开关）

- 中央库下载（git clone / zip）；按工具 copy 到各家 skills 目录 + manifest 哈希对账
- 不用 `~/.agents/skills` 共享目录（跨读泄漏，无法按工具开关）
- 启停 = 目录存在与否（Codex 可用 `[[skills.config]]` 禁用）

### F6 支路 Distiller Agent（蒸馏）

- **不自研 runtime**：复用一等 CLI 的 headless 模式（`codex exec` / `claude -p --output-format stream-json` / `opencode run`）跑 `distill` 类型后台 Job
- 输入：Harvested 会话（单条或批量）；产出三类：
  1. `summary.md` 精简会话（长会话压缩成可读交接）
  2. memory/handoff 增量建议
  3. SKILL.md 草案（从重复操作/教训中提炼）
- **全部进审阅队列，人审批准后**才写入 inject 真源或技能中央库（草案默认不对任何工具启用）
- 触发：手动（选中会话→蒸馏）+ 可选每日批量；显示 token 成本预估，可指定便宜模型

## 非目标

- 云同步 / 多机自动同步、团队协作（vault 本身是文件，可手动搬）
- 解密任何加密数据库
- 调度封闭 Work App 内部工作流
- 跨工具的 raw transcript 移植（只做语义迁移）
- 自研 agent runtime；Distiller 也只是 headless 调用现有 CLI；Pi 仅作注入范式参考
- Distiller 产物绕过人审直接生效

## 成功标准（验收）

1. 启动即 WPF 桌面窗口，终端可见地嵌在窗口内
2. 内嵌终端完整操作至少一家 TUI（优先 claude 或 opencode），中文 IME 可用
3. 同窗 Structured pane 展示同一会话的旁路 transcript
4. OpenCode 历史 327 会话可在归档页检索打开
5. Hub 写一段 handoff → 投影文件出现 → 新 CLI 会话能读到
6. 装一个 skill → 仅对指定工具启用 → 目录可见性符合预期
7. `dotnet test` 全程不弹真实终端/GUI
8. 一条会话完成后可一键 Harvest 进 vault；删掉工具侧文件（模拟换机）后归档页仍能完整回放
9. 选一条长会话 → 蒸馏 → 审阅队列出现 summary 与 handoff 建议 → 批准后投影 → 换一家 CLI 开新会话能接上下文（跨工具语义迁移闭环）
