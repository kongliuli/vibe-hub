# PENDING — vibe-hub 主控待办

> 编写模型: Claude Fable 5 (Cursor Agent) · 2026-07-21

工作入口：接任务先看本文件；spec 归档时同步勾掉对应条目。

## 进行中 / 待办

- [ ] **P1 终端 spike**：按 [specs/spec-001-terminal-spike.md](specs/spec-001-terminal-spike.md) 验证 EasyWindowsTerminalControl（IME 是关键关卡），失败则切 WebView2+xterm.js
- [ ] **P2 MVP 骨架**：按 [specs/spec-002-mvp-skeleton.md](specs/spec-002-mvp-skeleton.md) 搭 VibeHub.sln（Supervisor + OpenCode/Codex Adapter + 内嵌终端 + SQLite job 表）
- [ ] **P3 归档第一批**：OpenCode Adapter（`opencode.db` 只读）→ Structured pane 首个数据源；随后 WorkBuddy memory、Kimi memory vault、Trae skills（见 [design/03-archive-sources.md](design/03-archive-sources.md)）
- [ ] **P4 注入与 Skills**：Hub sink（收编进 vault）+ 管理块投影 + Skills copy/manifest（见 [design/04-inject-skills.md](design/04-inject-skills.md)）
- [ ] **P5 Cursor agent CLI**：本机安装 `agent`（`irm 'https://cursor.com/install?win32=true' | iex`）后接入第四家 Adapter
- [ ] **P6 真归档 vault + Harvest**：vault 布局 + ingest（raw+canonical 双层，哈希核对）+ 生命周期状态（见 [design/06-session-lifecycle.md](design/06-session-lifecycle.md)）
- [ ] **P7 Distiller 支路 agent**：`distill` Job（headless CLI）+ 审阅队列 + 迁移向导（跨工具语义迁移闭环）
- [ ] **补** Claude Adapter 落地时复核 Windows resume 冻结 bug 是否已修（anthropics/claude-code #23235 等）

## 明确不做（防跑偏）

- 不解密 Trae `database.db`（两库均 SQLCipher 类加密，已实测判红）
- 不调度 Kimi Work / WorkBuddy / Trae Work 内部任务与多 Agent 编排
- 不做云同步、多机、账号体系
- 不自研 agent runtime / LLM loop
- 终端不允许外置窗口方案回潮
