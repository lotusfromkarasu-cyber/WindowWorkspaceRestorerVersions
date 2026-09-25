# Runtime（运行时说明）

- `artifacts/publish/portable/` 是自包含版本，.NET 运行时已经打包进 EXE，不需要额外文件。
- `artifacts/publish/framework-dependent/` 依赖目标电脑已安装的 .NET 8 Windows
  Desktop Runtime（Microsoft.WindowsDesktop.App 8.x）。检查命令：

    dotnet --list-runtimes

- 自包含发布时，NuGet 会把运行时包下载到 `Dependencies\packages\`（例如
  microsoft.netcore.app.runtime.win-x64、microsoft.windowsdesktop.app.runtime.win-x64），
  它们属于依赖缓存，不属于本目录。
- 本目录目前只存放说明，不保存运行时文件。