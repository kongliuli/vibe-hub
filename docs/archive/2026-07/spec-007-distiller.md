# spec-007：Distiller 骨架（P7）

> 已落地并归档 · 2026-07-21（骨架；未真起 CLI）

> 编写模型: Cursor Grok 4.5 (Cursor Agent) · 2026-07-21

## 交付

- `ReviewQueue`：Pending / Approved / Rejected 文件队列
- `Distiller.BuildHeadlessSpec`：opencode/codex/claude/cursor-agent 无头契约
- `ProposeSummary`：从 canonical 消息生成草稿（不调模型）→ 入队 → 批准写 vault `summary.md` + `Distilled`
- App：`Distill draft` 按钮（MVP 自动批准）
- 单测 mock，禁止 `Process.Start`

## 完成定义

- [x] 队列 + 批准落盘
- [x] headless spec 契约
- [x] `dotnet test` 绿
- [x] PENDING P7 勾掉（真 CLI / 人审 UI 列为可选增强）
