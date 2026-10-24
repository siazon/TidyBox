# 磁盘分析模块 —— 树表视图 Implementation Plan

**Goal:** 把现在"一层扁平列表"的磁盘分析界面，改成参考 WizTree 的树表：可逐层展开的目录树、名称列带占比条、一排统计列、列头点击排序。

**参考图要素：** 目录树（任意深度、每层按大小降序）；`[N 个文件]` 虚拟行（汇总该文件夹下直接的文件）；名称列左侧显示"占上一层 %"并带按比例填充的背景条；列：大小、文件、文件夹、占上一层 %（带进度条）、上次修改。

## 已定的决定（与用户确认过）

- **范围：完整树表。** 包含展开/折叠、占比条、各统计列、列头排序、`[N 个文件]` 虚拟行。
- **不做"已分配"列**（磁盘实际占用）。只显示"大小"（文件实际字节数）。以后需要再加，不影响现在的数据模型结构。
- **历史快照只存顶层。** 保持现有 `DiskScanSnapshot`/`SnapshotStore`/`SnapshotDiff` 不变。树本身只在内存里，不持久化。因此"较上次"只有顶层文件夹有数据，更深的行显示为空。
- **不再有"与上次对比"开关**：改成树表里常驻的"较上次"列（顶层行有上次扫描时显示增减，否则为空）。数据来源仍是现有的 `DiskScanResult.Changes`。
- 不做 NTFS MFT 极速扫描（设计文档的非目标），仍用标准递归遍历。

## 与现状的关系

- 现有 `DirectoryScanner.ScanAsync`（扁平快照）保持签名和行为不变，它的测试继续有效；内部改为从树投影出顶层条目，避免遍历两次。
- WinUI3 没有现成的树表控件：引擎层提供"压平的可见行列表"（`DiskTreeModel`），界面用 `ListView` + 每行一个 Grid 来渲染，展开/折叠时对 `ObservableCollection` 增删行，保持滚动位置。
- 业务逻辑（树构建、占比、排序、展开状态）全部放在 `PowerTools.Modules.DiskAnalyzer`，可单测；页面代码只做绑定和事件转发。

## Tasks

### Task 1: 目录树数据模型 + 树扫描器
- `DiskNode`：名称、完整路径、`SizeBytes`、`FileCount`（子树内文件总数）、`FolderCount`（子树内文件夹总数）、`LastModifiedUtc`（子树内最新文件时间；无文件时用文件夹自己的时间）、`Kind`（Folder / FilesGroup）、`Children`（文件夹子节点 + 一个 `[N 个文件]` 虚拟节点，按大小降序）。
- `DirectoryScanner.ScanTreeAsync(rootPath, ct)` 返回根 `DiskNode`；根的直接子文件夹并行扫描。
- 沿用 Task 1 已验证的规则：`IgnoreInaccessible`、隐藏/系统文件计入、不跟随 reparse point、可取消。
- `ScanAsync` 改为由树投影得到顶层条目（行为不变）。

### Task 2: 树表视图模型 `DiskTreeModel`
- `DiskTreeRow`：节点、缩进层级、是否展开（`INotifyPropertyChanged`）、占上一层 %、格式化后的各列文本、较上次增减文本。
- `DiskTreeModel`：`Rows`（`ObservableCollection`）、`Expand/Collapse/Toggle(row)`、`SortBy(column)`（同一兄弟组内排序，再点一次反向，保持展开状态）、默认按大小降序、根行默认展开一层。
- 模块 `ScanAsync` 的结果带上树根，供界面构造 `DiskTreeModel`。

### Task 3: 界面
- 顶部：路径输入框 + "浏览…" + "扫描"（输入框也让界面可以被自动化驱动）。
- 树表：可点击的列头；每行：展开箭头、文件夹/文件图标、占比条、各统计列；选中高亮。
- Steam 游戏库放进第二个标签页，原有功能不变。
- 验证：构建通过；用 UI Automation 驱动并截取应用窗口，检查层级、占比条、排序；Steam 部分仍需有 Steam 的机器手动确认。

## 非目标

不做 Treemap 图形视图、不做"已分配"列、不持久化整棵树、不做 MFT 极速扫描、不做盘符列表选择。
