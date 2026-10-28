# 用户反馈功能——设计文档

日期：2026-10-28
状态：范围由用户定（见第 2 节），其余为我的决定，已标注；随 plan 一起交用户审阅。

## 1. 意图

在 PowerTools 里加一个"发送反馈"入口：用户写完点提交，请求发到作者自己的 Cloudflare Worker，存进 D1 数据库，作者事后自己查看。目的是在不依赖 Store 评论的情况下，直接拿到用户的建议和错误报告。

## 2. 用户已定

- 后端用 **Cloudflare Workers + D1**。
- 反馈内容：**文字 + 可选的联系方式 + PowerTools 版本号**。
- 用户选择"错误"类型时，**还要带上相关日志**。
- 先写 plan，不再逐项确认。

## 3. 我的决定（用户可改）

| # | 决定 | 理由 |
|---|---|---|
| 1 | 三种类型：建议 `suggestion` / 错误 `error` / 其他 `other` | 用户只提了"error"，其余两类让"非错误"反馈也有归处 |
| 2 | 选"错误"时出现复选框"附带最近的运行日志"，**默认勾选**，并可预览将发送的内容 | 满足"带上日志"，同时让用户知道、能拒绝；日志可能含路径，Store 隐私审核也更稳妥 |
| 3 | 日志发送前**脱敏**：用户目录路径→`%USERPROFILE%`，用户名→`<user>`，机器名→`<machine>` | 日志里的异常堆栈和路径会带用户名 |
| 4 | 日志只取最近 **64 KB**（在行边界截断） | 控制体积 |
| 5 | 服务器**只存 IP 的哈希**（SHA-256 + 服务端密钥盐），不存原始 IP | 仅用于防刷，减少隐私负担 |
| 6 | 不收集系统版本、硬件、设备 ID，只有 PowerTools 版本号 | 用户只要求版本号 |
| 7 | 防滥用：单个 IP 哈希每小时最多 **5** 条；全站每天最多 **2000** 条 | 桌面应用没法保管密钥，接口只能公开，靠限额兜底 |
| 8 | 发送失败（含网络不通）时对话框**保持打开、内容保留**，不做本地队列/自动重试 | 目标用户在中国大陆，`*.workers.dev` 可能连不上；本版先把失败说清楚，队列留到有需要时 |
| 9 | 反馈地址写成一个常量，**为空时隐藏整个反馈入口** | 在作者部署 Worker、拿到域名之前，功能不会暴露一个坏掉的入口 |
| 10 | 作者查看反馈用 `wrangler d1 execute` 与一个 `npm run list` 脚本，不做管理后台 | 只有作者一个人用 |

## 4. 目标、非目标、反目标

**目标**
- 设置页里有"发送反馈…"，弹出对话框：类型、内容、联系方式（可选）、（错误类型）附带日志复选框和预览。
- Worker 校验后写入 D1，返回明确的状态码。
- PowerTools 有一个最小的文件日志，记录启动、未处理异常、模块加载失败、任务栏单词条失败。
- 隐私声明（`docs/store-submission.md` 与一份可发布的短隐私说明）如实更新。

**非目标（本版不做）**
- 本地离线队列与自动重试；管理后台；回复用户；附件/截图；邮件通知；国际化。
- 收集系统版本等诊断信息。

**反目标（即使能跑也算失败）**
- 未经用户看到和同意，就把日志发出去。
- 日志里的用户名、用户目录路径、机器名原样出现在服务器上。
- 网络不通时界面卡死、崩溃，或用户写了很久的内容被清空。
- 接口可以被一个脚本用单 IP 无限写入。

## 5. 约束

- Windows 11 only；PowerTools 是 MSIX 全信任，普通权限运行。
- 新增网络访问意味着 Store 隐私声明从"不联网"变为"仅在用户主动提交反馈时联网"；Partner Center 表单要同步，可能要提供隐私政策链接。
- Worker 是 TypeScript，放在仓库的 `server/feedback-worker/`；测试用 `@cloudflare/vitest-plugin`（旧名 `@cloudflare/vitest-pool-workers` 已废弃；peer 为 `vitest ^4.1 || ^5`），`vitest` 取 `^4.1`。
- 部署需要作者自己的 Cloudflare 账号登录，以及一个托管在 Cloudflare 上的域名（大陆可达性）；这些步骤只能由用户完成。

## 6. 接口契约

`POST /v1/feedback`，`Content-Type: application/json`

| 字段 | 类型 | 规则 |
|---|---|---|
| `kind` | string | `suggestion` / `error` / `other` |
| `message` | string | trim 后 1–4000 个字符（UTF-16 长度） |
| `contact` | string? | 可选；trim，空串视为无；最长 200；不校验格式（邮箱/QQ/微信都可以） |
| `appVersion` | string | 形如 `1.2.3.4`（四段数字，每段 1–5 位） |
| `logs` | string? | 只允许 `kind == "error"`；UTF-8 最长 65536 字节 |

整个请求体最长 128 KB。

| 状态码 | 含义 | 响应体 |
|---|---|---|
| 201 | 已保存 | `{"id": <number>}` |
| 400 | JSON 无效或字段不合规 | `{"error":"invalid_request","field":"<字段名>"}` |
| 404 / 405 | 路径不对 / 不是 POST | `{"error":"not_found"}` / `{"error":"method_not_allowed"}` |
| 413 | 请求体或日志过大 | `{"error":"too_large"}` |
| 429 | 该 IP 哈希一小时内已达 5 条 | `{"error":"rate_limited"}` |
| 503 | 全站当天已达 2000 条 | `{"error":"busy"}` |

## 7. 数据（D1）

表 `feedback`：`id`（自增主键）、`created_at`（UTC，ISO 8601）、`kind`、`message`、`contact`（可空）、`app_version`、`logs`（可空）、`ip_hash`；`kind` 有 `CHECK` 约束；索引 `created_at` 和 `(ip_hash, created_at)`。用 D1 migrations 管理（`migrations/0001_init.sql`）。

## 8. 客户端

- `PowerTools.Core/Feedback/`：`FeedbackKind`、`FeedbackSubmission`、`FeedbackLimits`、`FeedbackResult`、`FeedbackClient`（`HttpClient`，10 秒超时）。结果映射：201→已发送；400/413→被拒绝；429/503→太频繁；其它状态码、网络错误、超时→无法连接。用户主动取消不算失败，照常抛 `OperationCanceledException`。
- `PowerTools.Core/Logging/`：`AppLog`（文件日志，`%LOCALAPPDATA%\PowerTools\logs\powertools.log`，超过 512 KB 滚动为 `.1.log`，只保留一份旧的；永不抛异常；每行同步写入，崩溃前也能落盘）和 `LogRedactor`（脱敏）。
- App 层：设置页新增"反馈"区与 `FeedbackDialog`；`FeedbackConfig.Endpoint` 常量；版本号来自 `Package.Current.Id.Version`。
- 埋点只有四处：启动（含版本号）、未处理异常（`App.UnhandledException`、`AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`）、模块加载失败、单词条无法显示。日志里**只有**这些，所以"相关日志"的覆盖面是有限的，这点要让用户预期到。

## 9. 错误处理

- Worker：任何校验失败都不写库；D1 写入失败返回 500，不泄露内部信息。
- 客户端：失败文案（固定）——已发送：`已发送，谢谢你的反馈。`；被拒绝：`内容不符合要求，请检查后再发送。`；太频繁：`发送太频繁，请稍后再试。`；无法连接：`无法连接到反馈服务器，请检查网络后重试。内容已保留。`
- `AppLog` 自身出错一律吞掉，日志不能让应用崩溃。

## 10. 测试与验证

- Worker：`vitest` + `@cloudflare/vitest-plugin`，用真实的 D1（Miniflare）应用 migrations，覆盖校验、限额、写库。
- Core：xUnit，`FeedbackClient` 用假的 `HttpMessageHandler`（不走真实网络）；`AppLog`、`LogRedactor` 用临时目录。
- 手动：用 `wrangler dev` 起本地 Worker，在 PowerTools（Debug 构建可用环境变量覆盖地址）里真实提交一条建议和一条带日志的错误，再用 `npm run list` 看库里的内容；部署后再做一次端到端。
- 普通 `dotnet build` 不会刷新已注册 dev 包的布局，需要运行 App 的验证按之前的做法同步 AppX 布局。

## 11. 留给实现者决定

- Worker 的文件划分与错误响应的细节写法；对话框的具体控件排版（文案已定）；`AppLog` 的行格式。
