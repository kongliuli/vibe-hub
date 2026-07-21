# spec-005：Cursor agent Adapter（P5）

> 已落地并归档 · 2026-07-21（骨架；本机 CLI 未装）

> 编写模型: Cursor Grok 4.5 (Cursor Agent) · 2026-07-21

## 交付

- `CursorAgentAdapter`：`Discover` / `BuildStart` / `BuildResume(--resume=id)` / `ListSessions`（扫 agent-transcripts）
- App Provider 下拉含 `cursor-agent`；未安装时提示安装命令
- Archive 源 `cursor-agent`（transcript 常 REDACTED；完整通道留给 stream-json）

## 安装

```powershell
irm 'https://cursor.com/install?win32=true' | iex
agent --version
```

## 完成定义

- [x] Adapter + 单测（假 exe 路径）
- [x] App 接入
- [x] 安装说明写入 design/02
- [x] PENDING P5 勾掉（运行时仍依赖用户安装 CLI）
