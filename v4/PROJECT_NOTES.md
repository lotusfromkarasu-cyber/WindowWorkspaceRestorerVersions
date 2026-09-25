# v4 源码说明

## 选择性恢复

`MainViewModel` 在选择 `WorkspaceRecord` 时生成独立的 `RestoreSelectionItemViewModel` 清单，默认全选。恢复前以勾选项创建临时记录并交给原有 `WorkspaceRestoreService`；持久化记录本身不会被删改。切换工作区会重新建立清单，扫描按钮切回“当前窗口”页。

## 既有恢复能力

`ExplorerAdapter` 通过 COM 对象身份识别新标签页，并等待导航稳定；`PowerPointAdapter` 从实际文档窗口读取并恢复演示文稿；`WpsAdapter` 枚举真实文档实例，以主窗口 HWND 对 WPS 文字、表格、演示标签分组。相关逻辑继承自已验证的 v3。

## 构建与持久化

目标为 .NET 8、WPF、win-x64。发布名为 `WindowWorkspaceRestorer-v4`，程序集版本 4.0.0。`PersistenceService` 以 `records-v4.json`、schema 4 独立存储；首次无 v4 记录时优先复制导入 v3。设置存入 `settings-v4.json`。

`Directory.Build.props` 将构建输出放入 `artifacts`。普通回归测试不会操作桌面；真实 Explorer、PowerPoint、WPS 测试需显式启用。
