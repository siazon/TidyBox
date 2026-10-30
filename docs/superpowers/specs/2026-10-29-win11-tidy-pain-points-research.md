# Win11「整理」方向的用户痛点与竞争格局调研

日期：2026-10-29
目的：从"整理（Tidy）"的角度，看 Windows 11 用户还有哪些没被满足的痛点，并判断 TidyBox 现有三个模块（右键菜单、磁盘分析、卸载）的位置。
性质：**市场调研，不是设计决策。** 文中"判断"和"建议"都是基于有限证据的假设。

---

## 0. 数据边界（先读这一节）

| 来源 | 实际拿到的 | 没拿到的 |
|---|---|---|
| Reddit | 约 20 条帖子的**标题和搜索摘要**，部分带点赞数 | 帖子正文和评论（被网络安全拦截，要求登录） |
| 知乎 | 搜索摘要 | 文章和回答正文（页面是壳） |
| 小红书 | 无可用结果 | 全部（公开网页路径返回的几乎都是无关内容，需要登录渠道） |
| V2EX | 搜索摘要 | 帖子正文（读取超时） |
| GitHub | 仓库的 star、更新时间、简介、许可证 | — |
| 英文/中文媒体 | IT之家、TechRadar、Microsoft Q&A 等的完整或部分内容 | — |

结论：本文能回答"需求有多大、对手有多强"，**不能回答"用户原话怎么说"**。目标市场（简体中文用户）的一手声音是最大缺口。

---

## 1. 第一轮：从"整理"方向看到的痛点

| 痛点 | 证据 | 强度 |
|---|---|---|
| 下载文件夹和桌面乱 | Reddit 至少 7 个相关帖（r/productivity、r/datacurator 27 赞、r/software、r/windowsapps）；**多个帖子是"我自己写了个整理下载文件夹的小工具"**，既是需求信号也是竞争信号 | 强 |
| 文件资源管理器慢、布局被改 | "最大的槽点是资源管理器性能"、"资源管理器 bug 汇总"（99 赞）、"Home/Gallery/OneDrive 被锁死"、"找回拖放到目录"（18 赞）。**多数是 Microsoft 自身问题，第三方工具难以修复** | 强 |
| 右键菜单两层、越来越长 | Reddit 标题、Microsoft Q&A 的恢复经典菜单方法；知乎文章称右键菜单可能排在日常痛点前三，并推荐 NileSoft Shell 一类的整理工具 | 强 |
| 启动项/后台程序多，不敢乱关 | Reddit 高赞回答是"下载 Autoruns"；知乎有"Win11 越用越卡，是默认设置在拖后腿"一类文章 | 中 |
| 对"清理类软件"不信任 | "替代 CCleaner"帖里有人反问"你真的需要这些工具吗"；"去臃肿安全吗"（42 赞）；知乎提醒第三方改造工具有风险 | 中 |
| C 盘爆满 | 仅找到知乎"开启存储感知"的零星建议，数据不足 | 弱 |

---

## 2. 第二轮：竞争格局（GitHub star + 媒体）

| 方向 | 数据 | 解读 |
|---|---|---|
| 右键菜单管理 | ContextMenuManager 20.3k★（最后更新 2024-08）；moudey/Shell 6.9k★；ContextMenuMgr 3.6k★（活跃） | 需求被证明是真的；中文社区头部是**免费 GPL** 工具 |
| 下载文件夹整理 | mouzi 1.1k★（MIT，活跃，2026-05 创建）；Store 有 Sortly、Folder Rules；File Arbor 一次性 $24.99（厂商博客，非独立验证） | 需求强，且已有付费定价锚点 |
| 磁盘分析/清理 | MangoDisk 5.0k★，自称 "safety-first disk cleaner and space analyzer"，活跃；SpaceSniffer 2k★ | MangoDisk 定位与 TidyBox 接近 |
| 卸载 + 残留 | prune、LeftoverSweeper（"Reversible quarantine"）、CleanLens 等，多为 2026-10 新建的小项目 | "可撤销"这个卖点已被一批新项目复制 |
| 桌面图标整理 | 开源替代品均不足 20★；V2EX 推荐 Fences（付费）等 | 没有强开源对手，但也可能是需求没那么大 |
| 启动项管理 | GitHub 没搜到代表性项目；Reddit 主流答案是 Autoruns | **数据空白** |

### Microsoft 自身的动向（对右键菜单模块的风险）

- Microsoft 已承认右键菜单过于杂乱（调高缩放后菜单一度占近 75% 屏高），并在 Build 26220.7271 推出 Split Context Menu 和"管理文件"（Manage file）子菜单。
- IT之家另有报道称 Microsoft 正在重构右键菜单并支持自定义（仅读到标题，正文未取得）。
- Shift+右键本来就能呼出经典菜单。

---

## 3. 判断（假设，不是结论）

1. **功能层面的差异化正在被快速抹平。** 2026-10 出现大量新建的零星小项目，覆盖卸载残留、下载整理、磁盘分析。"可撤销""有预览"这类单点卖点，对手一个周末就能复制，不能当护城河。
2. **右键菜单模块：需求最强，但付费意愿存疑。** 有 20k★ 的免费头部，Microsoft 也在修；"一键恢复经典菜单"这个免费钩子的价值在下降。价值应放在"整理和排序"，不是"恢复经典"。
3. **磁盘分析：** 必须靠设计文档里的两个差异点（跨次扫描历史对比、游戏/大型应用视图），否则就是反目标里的"又一个无差异化的磁盘分析器"。
4. **卸载：** 新进入者最密集，优先级可以后放。
5. **信任是行业共识，不是 TidyBox 独有的差异。** ContextMenuMgr 的简介写的是"避免第三方向你的右键菜单里塞屎"。
6. **可能被低估的是渠道**：头部工具大多靠 GitHub 分发，对不熟悉 GitHub 的中文用户有门槛。TidyBox 的真实优势可能是 Microsoft Store 分发 + 朋友的榜单推广。**此为推断，未查证 Store 里是否已有同类产品。**
7. **"自动整理下载文件夹/桌面"是需求最强、但不在现有设计文档范围内的方向。** 门槛低、竞品多，若做，差异点只能是"可预览、可撤销、不乱动"。

---

## 4. 未解决的缺口

1. 简体中文用户的真实声音（小红书、知乎正文）——需要用户本人通过 `agent-reach login xiaohongshu` 登录后才能读取。
2. Microsoft Store 里同类应用的评分、评论和定价。
3. 启动项管理、C 盘清理两个方向的数据。
4. Reddit 帖子正文和评论。

---

## 5. 下载/桌面整理类产品的功能拆解

数据来源是各产品官网或 GitHub 页面的摘要，**没有安装试用，也没读源码**。File Juggler、Hazel 的官网未写价格和安全机制，此处按"未知"处理。

| 类型 | 代表产品 | 思路 |
|---|---|---|
| A. 规则自动归档（Hazel 式） | Mouzi（开源）、File Juggler（付费）、SortSage（一次性付费）、DropIt（免费开源）、organize（MIT，命令行） | 监控文件夹，按规则移动、重命名、删除 |
| B. 桌面图标分区（Fences 式） | Stardock Fences（订阅制，金额未取到） | 只做视觉分组，不动文件 |
| C. 系统自带 | Windows 11 Storage Sense | 按时间阈值清理；选中 Downloads 的清理建议会删除整个文件夹 |

### 值得借鉴的点

- **Mouzi（最接近）**：Silent / Manual / Paused 三种模式；移动前预览且可勾选部分；SQLite 操作日志，单条或整次撤销；文件锁检测、宽限期、跳过 0KB、忽略系统文件、伪装 exe 告警；路径占位符 `{year}` `{month}` `{extension}`。自身路线图（未实现）：按压缩包内容分类、截图清理、从手动移动学习规则。
- **File Juggler**：按文档内容里的关键词或日期移动、重命名；自动解压下载的压缩包。
- **Hazel**：条件里有"文件来自哪个网站"；垃圾箱按天数或大小清理；App Sweep（卸载后清残留，与 TidyBox 卸载模块重合）。
- **DropIt**：拖放到悬浮图标处理；规则打包成 Profile。
- **organize**：先模拟再执行；EXIF 过滤；重复文件检测；空文件夹。
- **Fences**：Folder Portal、Quick-hide、Tabs。不移动文件，所以没有误删风险。

### 候选功能优先级（假设，待决策）

- **P0**：扫描并给出整理提案、移动前预览（可勾选）、操作日志与单条/整批撤销、安全过滤（文件锁、宽限期、0KB、系统文件、伪装 exe）。
- **P1**：手动/静默/暂停模式、占位符路径、内置默认分类、托盘 + 通知 + 自启、"来源网站"条件（见第 6 节，价值有限）。
- **P2**：旧文件提醒（复用磁盘模块的"多久没动过"数据）、EXIF 日期归档、自动解压（需防压缩炸弹和路径穿越）、Profile。
- **P3 / 不做**：内容识别与 OCR、桌面分区（Fences 已成熟且挂接桌面窗口复杂）、云端 AI 分类、自动删除。

### 判断

1. Mouzi 已经做到"预览 + 撤销 + 安全过滤"，且免费开源，这三点撑不起收费。
2. 可能的差异化是**跨模块联动**：整理时显示"下载文件夹占 X GB，其中 Y GB 是 N 天没动过的安装包"。单一整理工具做不到。
3. 许可证：Mouzi、organize 为 MIT，参考实现或使用代码时需保留版权声明并更新 `THIRD-PARTY-NOTICES.md`。DropIt 的许可证未核实。

---

## 6. 实测：`Zone.Identifier` 能否告诉我们"文件来自哪个网站"

测试日期：2026-10-29。方法：用 PowerShell 读取 `C:\Users\siazon\Downloads` 下各文件的 `Zone.Identifier` 备用数据流，提取 `HostUrl` 与 `ReferrerUrl`。

**样本局限**：全部来自作者一个人的下载习惯，PDF 仅 6 个、图片仅 8 个，只能作为方向参考。

### 覆盖率

| 文件类型 | 样本 | 有标记 | 有 ReferrerUrl | 来源的实用程度 |
|---|---|---|---|---|
| 安装包（.exe/.msi） | 39 | 3（8%） | 2 | 低 |
| 压缩包 | 15 | 15（100%） | 11 | 高 |
| PDF | 6 | 6（100%） | 3 | 中高 |
| 图片 | 8 | 6（75%） | 1 | 低 |

安装包覆盖率低的原因**未验证**（可能是解压、非浏览器下载或被解除锁定）。

### 发现

1. `HostUrl` 常是 CDN 域名（如 `kxcdn.com`、`release-assets.githubusercontent.com`、`drive.usercontent.google.com`），不能直接表示站点；`ReferrerUrl` 更有价值（如 `github.com`、`journals.plos.org`），但并不总是存在。
2. **`about:internet` 是占位值，不是网址**，PDF 与图片中共 4 个文件是这种情况。实现时必须识别并忽略。
3. 有意义的来源例子：`anpost.com`（物流面单）、`researchgate.net`（论文）、`web.whatsapp.com`（聊天软件收到的文件）、`github.com`（开源软件）。
4. 压缩包中出现非常规下载站（如 `mygrok.top`、`3dmgame.com`、`lanrar.com`），可作为"来源提示"。**只依据域名，没有检查文件内容，不能据此判断文件有害。**
5. 带 `(1)` 后缀的同名副本**不一定是浏览器重复下载**：样本所在的 `Downloads\Documents` 等目录已被 Mouzi 整理过，Mouzi 遇到同名冲突会自己加后缀（见第 7 节）。是否重复仍需比较内容，未验证。另外验证了被 Mouzi 移动过的文件（`.xpi`、`.apk`）`Zone.Identifier` 仍在，所以移动本身不会清掉来源标记；安装包覆盖率低的原因仍未查清。
6. 样本中 URL 均不带查询串，但样本太小，不能说明 token 泄露风险不存在。

### 对设计的影响

- 来源应作为预览里的**辅助信息列**，不作为分类规则的主条件。覆盖率不稳定会让用户觉得规则"时灵时不灵"。
- 实现要区分三种情况：完整 URL（取域名）、占位值（忽略）、无标记（留空）。优先 `ReferrerUrl`，没有再回退 `HostUrl`。
- 只存域名，不存完整 URL，不上传。
- 可能的小功能：按"来自聊天软件"归类、对非常规下载站给出提示。均基于单个样本，未下定论。

---

## 7. Mouzi 实机界面调研（截图）

材料：作者提供的 7 张 Mouzi 设置页截图（截图本身未纳入仓库，下面是摘要；版本推测为 0.2.1，依据是作者 `Installers` 目录里的安装包文件名，未确认）。**没有实际操作过，也没读源码**；只来自作者一台机器、一个版本。

### 界面结构

左侧 6 项：待批准、监视的文件夹、规则、历史记录、忽略、常规（另有"关于"）。

| 页面 | 观察 |
|---|---|
| 待批准 | 逐文件显示规则、动作、大小、目标路径；"可疑文件名和移至回收站的操作默认不勾选"；按钮是"检查所选操作"，不是直接执行 |
| 监视的文件夹 | 每个文件夹独立选模式（手动/静默/暂停）；"仅处理新文件"选项（跳过已有文件，重启后保持）；Google Takeout 归档导入 |
| 规则 | 默认 7 条（Images、Documents、Archives、Installers、Music、Videos、Others），每条有开关；可导出、导入，导入可选"替换/追加" |
| 规则编辑 | 名称、扩展名、正则、目标位置、动作、优先级；附加条件有大小、修改时间（UTC）；规范化扩展名；自定义通知 |
| 历史记录 | 按"整理批次 · 自动 + 时间"分组；按文件名、规则、扩展名、时间段筛选；可选整个批次；"检查撤销操作"二次确认 |
| 忽略 | 选择文件夹，模式写入该文件夹内的 `.mouziignore` |
| 常规 | 等待时间（默认 5 分钟）、检查文件占用、定时任务、开机启动、自动更新、资源管理器右键"Add to Mouzi" |

### 验证

- 被 Mouzi 移到 `Others` 的 `.xpi`、`.apk` 仍有 `Zone.Identifier`，移动不会丢来源标记。
- 截图 1 中同名冲突被改成 `GroupmealsBookings (1) (1).xlsx`、`Labels-CAP10325340 (1).pdf`，说明 Mouzi 对冲突只加数字后缀。

### 观察到的短板

1. "Others" 兜底文件夹成为杂物堆（作者目录中已有 33 个文件）。
2. 整理只是移入下载目录下的子目录（`Downloads\Images` 等），文件仍在同一棵树里。
3. 同名冲突只加后缀，不比较内容。
4. 大小显示原始字节（`6,200,472 B`）。
5. 界面里没有来源、"多久没动过"、空间统计。
6. 界面是简体中文，但默认规则名和目标文件夹是英文。
7. 规则条件时间用 UTC，历史记录用本地时间。
8. 配置（忽略规则）存放在被整理的文件夹内。

### 值得借鉴

预览页危险项默认不勾选；"仅处理新文件"；以批次为单位的历史与二次确认撤销；三种文件夹模式且默认手动；文件占用检查与等待时间；规则导出导入。

---

## 来源

- 整理类产品：Mouzi（github.com/hsr88/mouzi）、organize（github.com/tfeldmann/organize）、File Juggler（filejuggler.com）、DropIt（dropitproject.com）、Hazel（noodlesoft.com）、Stardock Fences（stardock.com/products/fences）、AlternativeTo 的 Hazel 与 SortSage 页面、Windows Central 关于 Storage Sense 的文章
- GitHub：BluePointLilac/ContextMenuManager、moudey/Shell、PLFJY/ContextMenuMgr、hsr88/mouzi、harry0703/MangoDisk、jimman0I/prune、HiddenOnLies/LeftoverSweeper
- IT之家：微软瘦身 Win11 文件管理器右键菜单（ithome.com/0/899/667.htm）；消息称微软正重构 Win11 右键菜单（ithome.com/0/984/464.htm）
- Microsoft Q&A：Restore old Right-click Context menu in Windows 11
- Reddit（标题与摘要）：r/Windows11、r/windows、r/productivity、r/datacurator、r/software、r/windowsapps
- V2EX：v2ex.com/t/1108794（整理桌面快捷方式，摘要）
- File Arbor 工具对比博客（filearbor.com）；Sortly（Microsoft Store）
- PowerToys issue #47594（集成 WizTree 式磁盘分析器）
