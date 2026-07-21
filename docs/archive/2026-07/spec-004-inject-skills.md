# spec-004：注入与 Skills（P4）

> 已落地并归档 · 2026-07-21

> 编写模型: Cursor Grok 4.5 (Cursor Agent) · 2026-07-21

依据 [design/04-inject-skills.md](../design/04-inject-skills.md)。最小可用：Hub sink 读写 + 管理块投影/拆除 + Skills 按工具 copy 启用/禁用 + manifest。

## 交付物

```
src/VibeHub.Core/Inject/
  ManagedBlock.cs       # begin/end 标记合并与拆除
  InjectSink.cs         # <LocalAppData>/vibe-hub/inject/<projectId>/{memory,handoff,context}.md
  InjectProjector.cs    # 投影到目标文件（先备份）
src/VibeHub.Core/Skills/
  SkillManifest.cs      # JSON manifest：hash / tool / enabled
  SkillInstaller.cs     # 整目录 copy / 删除禁用
tests + fixtures
```

## 完成定义

- [x] 管理块合并不覆盖用户原文；toggle off 只删管理块
- [x] Skills enable=copy、disable=删目标目录，manifest 可恢复信息
- [x] `dotnet test` 绿
- [x] PENDING P4 勾掉，本 spec 归档
