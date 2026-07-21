# vibe-hub

> 编写模型: Claude Fable 5 (Cursor Agent) · 2026-07-21

自用的 Windows 桌面 **AI coding agent 管理器**：在一个 .NET 10 WPF 窗口里调度（子进程 + ConPTY）、归档（只读旁路索引）、注入（memory/handoff 投影）与管理 Skills。

## 硬约束

- **.NET 10**（`net10.0-windows`），桌面 WPF GUI
- **终端控件必须内嵌**在主窗口内（禁止外置 Windows Terminal / 独立 console 当主路径）
- 调度只针对开放 CLI；封闭 Work 桌面 App（Kimi Work / WorkBuddy / Trae）**只读归档，不介入其工作流**
- 重拼装、小改造、自己用：能依赖现成开源就不重写

## 一等公民 CLI

| Provider | 调度 | 归档源 |
|---|---|---|
| OpenCode | `opencode` / `-c` / `-s <id>` | `~/.local/share/opencode/opencode.db`（明文 SQLite） |
| Codex | `codex` / `codex resume` | `~/.codex/sessions/**/*.jsonl` |
| Claude Code | `claude`（Win resume 有雷区） | `~/.claude/projects/**/*.jsonl` |
| Cursor agent | `agent` / `agent resume`（装到 `%LOCALAPPDATA%\cursor-agent`，与 IDE 分离） | stream-json 捕获为主；IDE `agent-transcripts` 常 REDACTED |

Pi 仅作「注入范式」参考，不进默认调度名单。

## 文档导航

工作入口：[docs/PENDING.md](docs/PENDING.md)

```
docs/
  PENDING.md                 # 主控 pending，唯一入口
  requirements/vision.md     # 产品定位与硬约束（目标）
  design/                    # 目标架构（现状应然）
    00-architecture.md       # 总览：三平面 + 双轨终端
    01-terminal-embedding.md # WPF 内嵌终端选型结论
    02-dispatch-adapters.md  # 四家 CLI Adapter 契约
    03-archive-sources.md    # 本机归档源勘察与可行性
    04-inject-skills.md      # 注入落点 + Skills 管理策略
    05-local-inventory.md    # 本机 C 盘 AI 目录盘点
  archive/2026-07/
    spec-001-terminal-spike.md
    spec-002-mvp-skeleton.md
    spec-003-archive-batch1.md  # 归档第一批（已落地）
```

## 当前状态

P1–P3 已落地：

```bash
dotnet build VibeHub.slnx
dotnet test
dotnet run --project src/VibeHub.App
```

左栏：Dispatch Provider（Start/Resume）+ **Archive Source**（OpenCode / Codex / WorkBuddy / Kimi / Trae skills / Trae 加密元数据）。右侧 Terminal | Structured。主线 P1–P7 骨架已齐（见 `docs/PENDING.md`）。Cursor `agent`：`irm 'https://cursor.com/install?win32=true' | iex`。vault 默认 `%USERPROFILE%\vibe-hub-vault`。
