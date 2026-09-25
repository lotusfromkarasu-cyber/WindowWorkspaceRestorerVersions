# Window Workspace Restorer

Windows 桌面工作区恢复工具，可保存并恢复 Word、Excel、PowerPoint、WPS、资源管理器文件夹标签组和 COMSOL 窗口。

## 下载

打开 [Releases](https://github.com/lotusfromkarasu-cyber/WindowWorkspaceRestorerVersions/releases/latest)，下载 `WindowWorkspaceRestorer-v4.exe`。这是适用于 Windows x64 的独立便携版，无需另行安装 .NET。

运行程序后扫描当前窗口、保存工作区；之后选择工作区和要恢复的项目，再点击恢复。v4 支持逐项选择恢复。

## 版本内容

- `v4/`：v4.0.0 源码、测试和构建脚本。
- `v3.7z`：v3 历史版本源码与程序归档。
- v4 编译后的便携 EXE 通过 GitHub Releases 分发。

本仓库快照没有 v1、v2 的版本目录。v4 使用独立记录文件 `%LocalAppData%\WindowWorkspaceRestorer\records-v4.json` 和 `settings-v4.json`；个人工作区数据不会随源码发布。

## 从源码构建

需要 Windows、.NET 8 SDK 和 Windows Desktop 组件。进入 `v4` 目录后运行：

```powershell
.\tools\build.ps1 -KeepPackages
.\tools\publish.ps1
```

源码位于 `v4/src`，测试位于 `v4/tests`。桌面集成测试需要相应的 Office、WPS 或 COMSOL 软件。

## 许可

当前仓库未附带开源许可证；公开源码不代表授予再分发或修改许可。
