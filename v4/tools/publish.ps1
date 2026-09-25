param([switch]$KeepPackages)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\WindowWorkspaceRestorer.App\WindowWorkspaceRestorer.App.csproj'
$portableDir = Join-Path $root 'artifacts\publish\portable'
$frameworkDir = Join-Path $root 'artifacts\publish\framework-dependent'
$packagesDir = Join-Path $root 'Dependencies\packages'
$binDir = Join-Path $root 'artifacts\bin'
$objDir = Join-Path $root 'artifacts\obj'
foreach ($dir in @($binDir, $objDir, $portableDir, $frameworkDir)) {
    if (Test-Path -LiteralPath $dir) {
        $resolvedTarget = [IO.Path]::GetFullPath($dir)
        $resolvedRoot = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
        if (-not $resolvedTarget.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Cleanup target outside project: $resolvedTarget" }
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
}

# 便携版：自包含单文件，运行时打包进 EXE
dotnet publish $project -c Release -r win-x64 --self-contained true -o $portableDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-ChildItem $portableDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

# 精简版：依赖目标电脑已安装的 .NET 8 Windows Desktop Runtime
dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o $frameworkDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-ChildItem $frameworkDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

if (-not $KeepPackages) {
    dotnet build-server shutdown | Out-Null
    try {
        if (Test-Path -LiteralPath $packagesDir) {
            $resolvedPackages = [IO.Path]::GetFullPath($packagesDir)
            $resolvedRoot = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
            if (-not $resolvedPackages.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Cleanup target outside project: $resolvedPackages" }
            Remove-Item -LiteralPath $resolvedPackages -Recurse -Force
            Write-Host "已删除 NuGet 缓存：$packagesDir"
        }
    }
    catch {
        Write-Warning "NuGet 缓存删除失败（可能被 Visual Studio 占用）：$($_.Exception.Message)"
    }
}