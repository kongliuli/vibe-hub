# 注入落点与 Skills 管理

> 编写模型: Claude Fable 5 (Cursor Agent) · 2026-07-21

## 一、各家落点事实表（本机实测 + 联网核对 2026-07-21）

| 工具 | 全局上下文 | 项目上下文 | Skills 目录 | symlink/junction | MCP 配置 |
|---|---|---|---|---|---|
| OpenCode | `~/.config/opencode/AGENTS.md`（本机未创建） | 沿途各级 `AGENTS.md` 拼接（全局在前） | `~/.config/opencode/skills/`、`.opencode/skills/`；**跨读** `.claude/skills` 与 `.agents/skills` | 未验证，按不支持处理 | `opencode.jsonc` 的 `"mcp"` 键（本机为 **JSONC 带注释**，写回须保注释） |
| Codex | `~/.codex/AGENTS.md`（**本机已有 8.7KB 手写内容**） | Git 根→cwd 沿途 `AGENTS.md`，root-down 拼接，默认 32KiB 上限 | 新 `~/.agents/skills/` + 旧 `~/.codex/skills/`（本机 17 个真实目录） | 旧路径**跳过 symlink**；新路径官方支持 | `~/.codex/config.toml` `[mcp_servers.*]`（本机已有 3 条，需 TOML 感知合并） |
| Claude Code | `~/.claude/CLAUDE.md`（本机未创建）；支持 `@path` import | `./CLAUDE.md` / `.claude/CLAUDE.md` / `CLAUDE.local.md` | `~/.claude/skills/`（本机未创建）、`.claude/skills/` | **最差**：junction/symlink 多个未修 bug（#41177、#68318、#38051）→ 只能 copy | 用户级 `~/.claude.json`（混有 OAuth 状态，**用 `claude mcp add --scope user` 写**）；项目级 `.mcp.json` |
| Cursor | 无全局上下文「文件」（User Rules 在应用设置）；本机有非官方通道 `~/.cursor/plugins/local/global-rules/rules/*.mdc` | `AGENTS.md`、`.cursor/rules/*.mdc`（alwaysApply/globs） | `~/.cursor/skills/`（本机未创建）；兼容读 `.agents/.claude/.codex` 的 skills；**勿动 `skills-cursor/`** | 2.5 桌面版已修，CLI 2026.02 后可用；老版本 copy | 全局 `~/.cursor/mcp.json`（本机存在且 `mcpServers` 为空）；项目 `.cursor/mcp.json` |

## 二、注入（Inject）设计

### Hub sink（权威源）

```
<vibe-hub-data>/inject/<projectId>/
  memory.md      # 长期偏好/事实
  handoff.md     # 交接：上次做到哪、下一步
  context.md     # 当前任务上下文
```

### 投影规则：管理块合并（核心决策）

目标文件是用户/团队手写载体（本机 `~/.codex/AGENTS.md` 即活例），**禁止整文件覆盖、禁止整文件 junction**。

```markdown
<!-- vibe-hub:begin (managed, do not edit) -->
...投影内容...
<!-- vibe-hub:end -->
```

- 目标已存在 → 追加管理块至末尾；不存在 → 创建仅含管理块的文件；关闭投影 → 只删管理块
- 首次写入前备份原文件；manifest 记录写入后哈希，检测用户手改漂移
- 各家最省通道：
  - Claude：注入一行 `@<sink路径>`（官方 import），Hub 改内容无需再碰目标
  - Codex：管理块写 `~/.codex/AGENTS.md`，注意 32KiB 总量，内容要精简
  - OpenCode：稳妥走 AGENTS.md 管理块（`instructions` 字段 V2 暂不生效）
  - Cursor：项目级生成独立 `.cursor/rules/vibe-hub.mdc`（alwaysApply），零冲突；全局级默认不做（非官方插件通道标注风险后可选）

### API 形状

```
inject.write(projectId, kind, content)        // 写 sink
inject.project(projectId, targets[])          // 投影（管理块合并）
inject.toggle(target, on|off)                 // 断开投影不删 sink
```

## 三、Skills 设计（下载 + 开关）

### 范围收窄

做：中央库下载（git/zip）、按工具启用/禁用、已装清单与状态。
不做：skill 商店/编辑器/运行时；「一键同步所有工具」叙事。

### 规则

1. **每工具目录整目录 copy**，不用共享 `~/.agents/skills`（OpenCode/Cursor 跨读会泄漏，无法按工具开关；Codex 同名不去重）
2. 映射：Claude → `~/.claude/skills/<skill>/`（junction bug，必 copy）；Codex → `~/.codex/skills/<skill>/`（跳过 symlink，必 copy）；Cursor → `~/.cursor/skills/<skill>/`（统一 copy 兼容老版本）；OpenCode → `~/.config/opencode/skills/<skill>/`
3. manifest：每技能记录哈希 + 来源版本；目标被手改（哈希不符）→ 提示 覆盖/保留/diff，不默默覆盖
4. 禁用：删目标目录（manifest 留档可恢复）；Codex 另可走 `config.toml` `[[skills.config]]` 不删目录
5. 跨读泄漏检测：某 skill 只给 Claude 启用但用户同时用 OpenCode/Cursor 时，UI 提示「其它工具也会看到」

### MCP（后期，给各家挂统一 memory server 时）

| 工具 | 操作 |
|---|---|
| Cursor | `~/.cursor/mcp.json` 键级合并（本机为空，零冲突） |
| Codex | `config.toml` 追加 `[mcp_servers.vibe-hub]`，TOML 感知、不重排既有内容 |
| Claude | 不直接编辑 `~/.claude.json`，调用 `claude mcp add --scope user` |
| OpenCode | `opencode.jsonc` 用 JSONC 库读写，保留注释 |

## 四、总原则

**上下文 = 文本管理块合并；Skills = 每工具整目录 copy + manifest 哈希对账；MCP = 键级合并。** junction 仅当「vibe-hub 完全拥有整个目录且目标工具明确支持」才考虑，默认全 copy。
