# spec-001：内嵌终端 Spike

> 编写模型: Claude Fable 5 (Cursor Agent) · 2026-07-21

目标：在 `net10.0-windows` WPF 窗口内跑通一家 CLI 的完整 TUI，确定终端控件最终选型。产出为可运行的 spike 工程 + 选型结论（回写 [design/01-terminal-embedding.md](../design/01-terminal-embedding.md)）。

## Spike 1：EasyWindowsTerminalControl（预算半天）

1. 新建 `spikes/TerminalSpike.EWTC`（WPF, net10.0-windows），装 NuGet `EasyWindowsTerminalControl`
2. `StartupCommandLine` 依次跑 `claude`、`opencode`
3. 逐项打勾验收：
   - [ ] alt-screen TUI 完整渲染（输入框、spinner、diff、`/` 菜单、退出无残影）
   - [ ] 键盘：方向键 / Tab / Esc / Ctrl+C / Shift+Enter；`Win32InputMode` 开关各测
   - [ ] 鼠标：opencode 中点击/滚轮生效
   - [ ] **中文 IME**：微软拼音 + 搜狗，候选窗位置 / 丢字 / 崩溃（生死项）
   - [ ] 截流：`GetConsoleText` 原始 VT 与 strip 文本双流
   - [ ] airspace：终端上叠 WPF Border 确认遮挡；Popup 弹层可用
4. 通过标准：IME 可用 且 airspace 可被 Popup/分栏布局绕开 → **定案方案②，spike 结束**

## Spike 2：WebView2 + xterm.js（预算 1–2 天，仅 Spike1 失败时）

1. 先 `dotnet run` 跑一遍 [CodeShellManager](https://github.com/umage-ai/CodeShellManager) 验证体验
2. 抄三件套：`PseudoTerminal.cs`（ConPTY P/Invoke）、`TerminalBridge.cs`、`Assets/terminal.html`；xterm.js 用 ≥2026-03 的 6.x（含 IME 修复 #5454）
3. 追加验收：
   - [ ] Spike1 全套 TUI/键盘/鼠标
   - [ ] IME：候选窗贴光标、占位文本场景首次组合、搜狗首字不丢
   - [ ] 加速键 `PreviewKeyDown` 桥接后全局快捷键与 TUI 快捷键不冲突
   - [ ] 4 并发 claude session 内存 / 大输出吞吐
   - [ ] PTY 早于页面加载时首屏输出不丢

## Spike 3：VirtualTerminal（可选，半天，前两者均硬伤时）

- 装 `VirtualTerminal.WPF` + `.CommandLine` 直接跑 claude；核心只验 alt-screen 复杂 TUI 渲染正确性；发现 parser 级问题立即止损。

## 约束

- spike 工程放 `spikes/`，不进 `src/`；结论回写 design 后 spike 代码可留作参考
- spike 属手动验证，不写自动化测试；正式代码期的单测禁真 ConPTY（vision C5）

## 完成定义

- [ ] 选型结论 + 实测记录回写 design/01
- [ ] `PENDING.md` P1 勾掉，本 spec 移入 `docs/archive/`
