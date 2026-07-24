# spec-008：归档、Skills 与 Inject 闭环

> model: gpt-5.6-sol | reasoning_effort: high | model_date: 2026-07-24

> 已落地并归档 · 2026-07-24

## 目标

1. Archive 的列表、消息读取和 Harvest 不阻塞 WPF Dispatcher；快速切换时旧结果不得覆盖新选择。
2. Codex Harvest 将原始 `rollout-*.jsonl` 原样复制进 Vault `raw/`。
3. Skills 页面直接调用现有 `SkillInstaller`，支持安装/启用、停用、漂移提示与保留备份的修复。
4. Inject 的 `memory.md`、`handoff.md`、`context.md` 以 Vault `projects/<projectId>/` 为真源；投影机制不变，旧 LocalAppData 内容只迁入、不删除。

## 最小设计

- UI 后台工作使用 `Task.Run`；后台不访问 WPF 控件，返回 UI 后按 source/session identity 丢弃过期结果。
- 后台 Harvest 使用独立 `VaultIndex` 连接，避免跨线程复用现有 SQLite connection。
- `CodexArchiveSource` 复用一个 rollout 路径查找函数；canonical 继续解析，raw 直接 `File.Copy`。
- Skills 不新增商店、下载器或插件层；用户选择本地 `SKILL.md` 和目标工具。
- 漂移修复先把现有目标移到同级 `.vibe-hub-drift-*` 备份，再从 manifest 来源重装。
- `InjectSink` 默认指向 Vault projects 根；首次使用时复制旧 sink 中缺失的文件，不覆盖 Vault 内容。

## 验收

- [x] Archive 列表、Structured、Harvest、Distill 的归档 I/O 不在 UI 线程执行。
- [x] Codex fixture 的 raw 导出与源文件字节一致，Harvest 后 Vault raw 存在。
- [x] Skills UI 可执行安装/启用、停用和漂移修复，底层安全测试通过。
- [x] Inject 默认路径位于 Vault，旧内容可无损迁入，投影测试通过。
- [x] Release build 与完整测试通过。

## 不做

- 不做 MVVM 大重构、Skill 商店、网络下载、vector DB、多实例租约或新的后台队列。
