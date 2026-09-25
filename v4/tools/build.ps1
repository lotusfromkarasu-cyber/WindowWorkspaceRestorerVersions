param([switch]$KeepPackages)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$packagesDir = Join-Path $root 'Dependencies\packages'
$binDir = Join-Path $root 'artifacts\bin'
$objDir = Join-Path $root 'artifacts\obj'
foreach ($dir in @($binDir, $objDir)) {
    if (Test-Path -LiteralPath $dir) {
        $resolvedTarget = [IO.Path]::GetFullPath($dir)
        $resolvedRoot = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
        if (-not $resolvedTarget.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Cleanup target outside project: $resolvedTarget" }
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
}

dotnet restore (Join-Path $root 'WindowWorkspaceRestorer.sln')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build (Join-Path $root 'WindowWorkspaceRestorer.sln') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test (Join-Path $root 'WindowWorkspaceRestorer.sln') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

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