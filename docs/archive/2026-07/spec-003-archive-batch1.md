# spec-003：归档第一批（P3）

> 已落地并归档 · 2026-07-21

> 编写模型: Cursor Grok 4.5 (Cursor Agent) · 2026-07-21

前置：P2 Structured 已能读 OpenCode / Codex。本批按 [design/03-archive-sources.md](../design/03-archive-sources.md) 把其余 🟢/🟡 只读源接到同一 Structured 入口。

## 交付物

```
src/VibeHub.Core/Archive/
  IArchiveSource.cs          # 统一枚举「会话/文档」+ 拉消息/正文
  WorkBuddyMemorySource.cs   # ~/.workbuddy/memory/*_memory.md
  KimiMemoryVaultSource.cs   # %APPDATA%/kimi-desktop/.../memory/vault/*.md
  TraeSkillsSource.cs        # ~/.trae-cn/skills + builtin_skills
  TraeEncryptedDbProbe.cs    # 仅元数据：存在/大小/mtime，判加密
tests/…Fixtures + 单测
VibeHub.App Structured：源下拉切换
```

## 契约

```csharp
interface IArchiveSource {
  string SourceId { get; }           // workbuddy-memory | kimi-vault | trae-skills | …
  string DisplayName { get; }
  bool Discover();                   // 路径是否存在
  IReadOnlyList<ArchiveEntry> List(int limit = 100);
  IReadOnlyList<CanonicalMessage> GetMessages(string entryId, int limit = 500);
}

record ArchiveEntry(string Id, string SourceId, string Title, string? Path, DateTimeOffset? UpdatedAt, string Kind);
// Kind: "memory" | "skill" | "session" | "encrypted-meta"
```

## 规则

- 全部只读；SQLite 先复制再开（与 OpenCode 相同）
- 显式跳过 `token-store.json` 等凭证路径
- Trae `database.db`：**不解密**；UI 显示「加密不可读」+ 打开目录
- WorkBuddy / Kimi 会话表若 0 行：List 返回空或仅 memory，不报错
- 单测：fixture md 解析；禁止真起 GUI / 真连用户库写

## 完成定义

- [x] 三源 Discover + List + GetMessages（或 skill 正文）可测
- [x] App Structured 可切换 OpenCode / Codex / WorkBuddy / Kimi / Trae skills
- [x] Trae 加密库元数据卡可见
- [x] `dotnet test` 绿
- [x] 勾掉 PENDING P3，本 spec 归档
