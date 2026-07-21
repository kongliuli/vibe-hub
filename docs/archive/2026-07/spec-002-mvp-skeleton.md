# spec-002：MVP 骨架

> 已落地并归档 · 2026-07-21（代码：VibeHub.App / Core / Terminal；`dotnet test` 绿）

> 编写模型: Claude Fable 5 (Cursor Agent) · 2026-07-21

前置：spec-001 已定终端选型。目标：一个可日常自用的最小闭环——左栏选项目/任务，右侧内嵌终端跑 OpenCode/Codex，Structured pane 显示旁路 transcript，job 落 SQLite。

## 交付物

```
VibeHub.sln
  src/VibeHub.App/          # WPF：MainWindow = 左栏 + 右侧 Terminal|Structured Tab
  src/VibeHub.Core/
    Supervisor/             # JobSupervisor + IProcessLauncher
    Adapters/               # OpenCodeAdapter, CodexAdapter（IProviderAdapter）
    Storage/                # SQLite: project/task/session/job 表 + FTS5
  src/VibeHub.Terminal/     # IPseudoTerminal + spec-001 选定控件适配
  tests/VibeHub.Core.Tests/
```

## 接口契约

```csharp
interface IProviderAdapter {
    string ProviderId { get; }                       // "opencode" | "codex" | ...
    bool Discover();                                  // 探测 CLI 与数据根
    ProcessStartSpec BuildStart(string cwd);
    ProcessStartSpec BuildResume(string cwd, string sessionId);
    Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(string? cwd);
}

interface IProcessLauncher {                          // 单测唯一 mock 点
    IPseudoTerminal Launch(ProcessStartSpec spec);
}
```

- OpenCode resume：`opencode -s <ses_…>`；session 发现走 `opencode session list --format json`（cwd 范围）或 spawn 后轮询抓新 id
- Codex resume：`codex resume <id>`；session 发现扫 `~/.codex/sessions/**` JSONL 头部

## Structured pane（最小版）

- 数据源：OpenCode `opencode.db`（只读副本）+ Codex JSONL tail
- 渲染：ItemsControl + Markdig；user/assistant 气泡、tool call 折叠行
- 刷新：文件 mtime 轮询（2s），MVP 不上 FileSystemWatcher 复杂度

## 单测（Ponytail：一个 runnable check 起步）

- `JobSupervisor_Start_PassesCorrectCwdAndArgs`：mock `IProcessLauncher`，断言 OpenCode start/resume 的 args 与 cwd
- `CodexAdapter_ParsesRolloutJsonl`：fixture 文件解析出 Message 列表
- 禁止：真起 ConPTY、真起 GUI、`Process.Start` 系统 exe

## 完成定义

- [x] 双 provider 可 start/resume，job 状态入库
- [x] Structured pane 显示真实历史会话
- [x] `dotnet test` 绿且无窗口弹出
- [x] 勾掉 PENDING P2，本 spec 归档
