# Window Workspace Restorer v4

记录和恢复 Word、Excel、PowerPoint、WPS 文字/表格/演示标签页、资源管理器文件夹标签组及 COMSOL 模型。

## 使用

直接运行 `artifacts\publish\portable\WindowWorkspaceRestorer-v4.exe`，无需安装 .NET。

点击“扫描窗口”，勾选项目并保存。点击左侧已保存工作区后，程序会自动打开“恢复内容”页；可逐项勾选，也可使用“全选”或“清空”，最后点击“恢复所选内容”。未勾选的项目不会打开。

精简版位于 `artifacts\publish\framework-dependent`，运行时请保留该目录全部文件，并安装 .NET 8 Windows Desktop Runtime。

## v4 更新

- 已保存工作区支持逐项选择恢复，默认全选。
- 点击左侧工作区会自动显示其文件、文件夹和标签页清单。
- 提供全选、清空、所选数量提示和“恢复所选内容”按钮。
- 保留 v3 的 macOS 风格界面、关闭到托盘选项、明确退出入口和单实例唤回。
- 保留资源管理器标签页、PowerPoint 文档窗口及 WPS 文字/表格/演示混合标签恢复。

## 版本与记录

本目录为 v4。仓库根目录另附 v3 历史版本压缩包；本仓库快照没有 v1、v2 的版本目录。

v4 独立读写 `%LocalAppData%\WindowWorkspaceRestorer\records-v4.json`（schema 4）和 `settings-v4.json`。自己的记录文件不存在时，依次尝试导入 `records-v3.json`、`records-v2.json`、`records-v2-comsol.json`、`records.json`；导入不会修改源文件。导入后各版本独立读写。

记录的是文件/文件夹及 Explorer、WPS 窗口分组；不记录窗口坐标、当前幻灯片或播放进度。

## 构建与测试

```powershell
.\tools\build.ps1 -KeepPackages
.\tools\publish.ps1
```

源码在 `src`，测试在 `tests`，最终程序在 `artifacts\publish`。普通测试不会操作桌面窗口，真实 Explorer、PowerPoint 和 WPS 测试默认跳过。

桌面集成测试需要本机相应软件；WPS 测试要求启用整合模式：

```powershell
$env:WWR_DESKTOP_TESTS = '1'
dotnet test .\WindowWorkspaceRestorer.sln -c Release --filter "Category=Desktop|Category=WpsDesktop"
Remove-Item Env:\WWR_DESKTOP_TESTS
```
