# spec-006：vault + Harvest（P6）

> 已落地并归档 · 2026-07-21

> 编写模型: Cursor Grok 4.5 (Cursor Agent) · 2026-07-21

## 交付

- `VaultPaths`：默认 `%USERPROFILE%\vibe-hub-vault/projects/<id>/sessions/<sid>/{raw,canonical.jsonl,meta.json}`
- `Harvester.Ingest` / `IngestFromArchive`：复制 raw 文件、写 canonical、记录 sha256 与 `SessionLifecycle`
- App：Archive 条目上「Harvest→vault」
- 单测：`HarvesterTests`

## 完成定义

- [x] vault 布局可创建
- [x] ingest 哈希 + Harvested / IngestError
- [x] `dotnet test` 绿
- [x] PENDING P6 勾掉
