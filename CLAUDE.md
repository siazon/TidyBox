# PowerTools（PC维护工具箱）项目说明

## 项目是什么

Windows Store 侧项目，面向简体中文/中国区市场。免费核心功能获客、付费Pro功能解锁的变现模式。第一个产品是"PC维护工具箱"：磁盘空间分析、右键菜单管理、卸载工具三个模块捆绑在一个App里。

**权威文档，改动前先看：**
- 产品设计文档：`docs/superpowers/specs/*.md`
- 实现计划：`docs/superpowers/plans/*.md`

## 技术栈与硬约束

- .NET 8，WinUI3，MSIX全信任打包（`runFullTrust`）
- **只支持 Windows 11**，不做 Windows 10 兼容
- App本身以普通用户权限运行，不声明`requireAdministrator`；只有真正需要系统级权限的操作才临时拉起一个提权帮手进程（命名管道IPC），用完即退出
- 不搭建对外的第三方插件分发/沙箱基础设施；内部模块按"松耦合组件、清晰接口、引擎层与UI层分离"设计，每个业务模块实现`IToolModule`接口
- **不把GPL许可证项目的代码直接编译进本项目**（参考过的GPL项目：ContextMenuManager、ContextMenuMgr、QuickLook、tiez-clipboard——只能借鉴设计思路，自己重新实现）；优先用MIT/Apache2.0的参考实现（如BCUninstaller是Apache2.0，可直接参考甚至复用代码）

## 工作流约定

- 新功能走 `brainstorming` → `writing-plans` → `subagent-driven-development` 这套流程，不要跳过设计文档直接写代码
- 每个实现计划在独立的git worktree（`.worktrees/<分支名>`）里执行，不要直接在`master`上开发
- commit message前缀：`feat:`/`fix:`/`docs:`/`chore:`
- 每个任务严格走TDD：先写失败的测试，确认失败，再实现，确认通过，再commit

## 当前进度

- 脚手架与共享基础设施计划（`2026-10-24-pc-toolbox-scaffolding-and-shared-infra.md`）：执行中
- 磁盘分析模块计划（`2026-10-24-pc-toolbox-disk-analyzer-module.md`）：已写好，待执行
- 右键菜单管理、卸载工具模块：计划还没写
