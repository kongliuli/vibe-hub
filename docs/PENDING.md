# PENDING — vibe-hub 主控待办

> model: gpt-5.6-sol | reasoning_effort: high | model_date: 2026-07-21

工作入口：接任务先看本文件。界面中的 `REAL` 表示已经连接现有服务，`MOCK` 只提供布局与交互占位，不代表功能完成。

## 本轮页面重构

- [x] 接入 HandyControl 3.5.1 暗色资源。
- [x] 按设计稿重构为「导航轨 / 项目与任务 / Agent 工作台 / 上下文检查器」布局。
- [x] 新增 `MainWindowViewModel`，集中承载页面 Mock 状态。
- [x] 保留真实的 Start、Resume、Kill、Archive、Structured、Vault Search、Harvest、Distill、Review、Inject 和 Migration 入口。
- [x] 终端创建后自动切到 Terminal Tab，并保留原键盘转发与焦点处理。
- [x] 侧边栏导航可用；Agent、Skills、设置拆为独立页面，不再混入会话上下文。
- [x] 设置页持久化默认 Provider、工作目录、新终端自动聚焦，以及 OpenCode 任务默认智能体和模型。
- [x] 会话工作台顶部 Provider/Job 条读取当前选择与 Supervisor 真状态；新建任务入口转到真实项目任务页，不再显示假 Running 时长。

## 已连接真实功能

- [x] OpenCode、Codex、Cursor agent 的启动与恢复 Adapter。
- [x] EasyWindowsTerminalControl 内嵌终端。
- [x] OpenCode、Codex、Cursor、WorkBuddy、Kimi、Trae 的只读归档源。
- [x] OpenCode 归档改用 SQLite WAL 只读直连；不再为每次刷新复制本机约 1.09 GB 的 `opencode.db`，首帧阻塞已消除。
- [x] Vault Harvest、canonical JSONL、meta、哈希与 FTS5 搜索。
- [x] Distill → Pending Review → Approve → `summary.md`。
- [x] Memory/Handoff sink 与 OpenCode/Codex 管理块投影。
- [x] 跨工具语义迁移向导。
- [ ] Agent 独立页已实现直接模型/Sisyphus 任务、模型覆盖、取消进程树及结果输出；`opencode-go/deepseek-v4-flash` 直接模型 smoke 已通过，Sisyphus 仍被缺少 `hephaestus` 子 Agent 阻断。

## 当前 Mock，必须继续落地

- [x] 项目页与项目树读取 `HubStore.Project` 和真实文件系统一级快照，不再展示硬编码目录。
- [x] 任务列表读取当前项目的 `HubStore.TaskItem`，支持创建任务及 `Todo → InProgress → Done` 状态流转，不再展示虚假进度。
- [x] Agent 活动流：由 Supervisor Job 启动/退出、自动 Harvest 与 Agent 独立页任务事件驱动，不再展示演示时间线。
- [x] Changes：只读调用 Git CLI，展示当前工作区 tracked/untracked 文件与可用的增删行统计，并同步真实分支和变更数到底栏。
- [ ] Agent 页面：CLI 可用性与 Model 覆盖已接真并可从设置持久化；Reasoning、Sandbox 与 Job 配置仍待接入。
- [ ] Context 使用量与文件 chips：接入各 CLI 的真实 token/context 信息。
- [ ] Skills 页面：已读取 `SkillManifestStore` 真数据；真实安装、启停和漂移修复操作仍待接入。
- [ ] Composer：接入当前 PTY 或 headless Job；当前输入框禁用发送。
- [ ] 底部状态栏：Git 分支与变更数已接真；测试、Vault 和 Agent 实时状态仍待接入。
- [ ] Memory 开关：接入投影 toggle；当前开关仅展示，真实写入仍使用显式按钮。
- [ ] 项目独立页已接 HubStore 真数据；Vault、Memory 独立页仍暂留 PENDING，真实功能还在会话工作台。

## P0 功能债务

- [x] 修复终端进程真实 PID、自然退出、Kill 进程树与退出码；EWTC 进程现由 watcher 跟踪，并由 Supervisor 幂等完成退出。
- [x] 多 Job 终端切换与 Focus；Job 列表现在保留每个 EWTC 控件，并切换当前 TerminalHost 与键盘焦点。
- [ ] 将 MainWindow 的服务编排和事件处理逐步迁移到命令/服务型 MVVM，保留 Dispatcher 边界。
- [x] Claude Adapter：接入 Start、`--resume <session-id>`、本地 projects JSONL 会话列表与归档读取。
- [ ] 经用户批准启动 UI 后，复核 Claude 在 EWTC/ConPTY 下的 Windows 交互 Resume 冻结问题；上游仍有 pseudo-terminal 冻结报告。
- [ ] UI 中 Job、Harvest、Migration、Inject 已统一使用当前 `HubStore.Project.Id`；Inject sink 内容仍需进入 Vault 真源。
- [ ] Codex/OpenCode Harvest 保存真正的 raw 会话或行导出，不只保存消息快照。

## UI 验证

- [ ] 人工启动 App，检查 1180×720 到 1920×1080 下的布局与滚动。
- [ ] 验证 Terminal 的 Ctrl+C、Ctrl+P、Tab、方向键、`/`、鼠标焦点和中文 IME。
- [ ] 验证 HandyControl 暗色资源不会覆盖 Terminal 的快捷键或焦点。
- [ ] 为主窗口增加最小 smoke 测试或可测试的 ViewModel 状态测试。

## 明确不做

- 不解密 Trae 数据库。
- 不调度封闭 Work App 内部任务。
- 不自研 Agent runtime 或 LLM loop。
- 不让 Mock 操作写用户文件、修改 Vault 或显示虚假的成功结果。
