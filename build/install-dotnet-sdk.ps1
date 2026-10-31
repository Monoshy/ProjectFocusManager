# ============================================================
#  Project Focus Manager —— .NET 10 SDK 安装脚本（用户级，无需管理员）
# ------------------------------------------------------------
#  作用：
#    1. 从微软官方地址下载 dotnet-install.ps1
#    2. 把 .NET 10 SDK 安装到 D:\dotnet（不写系统 PATH、不碰注册表服务）
#    3. 安装 NuGet 缓存目录到 D:\nuget-packages（节省 C 盘空间）
#
#  用法（在 PowerShell 里执行）：
#    powershell -NoProfile -ExecutionPolicy Bypass -File .\build\install-dotnet-sdk.ps1
#
#  说明：
#    - 这是微软官方提供的安装脚本，不是第三方来源。
#    - 安装完成后不会修改系统环境变量，只在当前窗口临时生效。
#    - 如需卸载：直接删除 D:\dotnet 目录即可（不影响系统其他部分）。
# ============================================================

param(
    [string]$InstallDir   = 'D:\dotnet',
    [string]$NuGetPackages = 'D:\nuget-packages',
    [string]$Channel      = '10.0'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

Write-Host "==> 目标安装目录: $InstallDir"
Write-Host "==> NuGet 缓存目录: $NuGetPackages"

# 1) 下载官方安装脚本
$scriptPath = Join-Path $env:TEMP 'dotnet-install.ps1'
Write-Host "==> 下载微软官方 dotnet-install.ps1 ..."
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $scriptPath -UseBasicParsing
Write-Host "    已保存到 $scriptPath"

# 2) 安装 SDK（-NoPath 表示不修改系统 PATH）
Write-Host "==> 开始安装 .NET SDK $Channel（约 200MB，请耐心等待）..."
& $scriptPath -Channel $Channel -InstallDir $InstallDir -NoPath

# 3) 创建 NuGet 缓存目录
if (-not (Test-Path $NuGetPackages)) {
    New-Item -ItemType Directory -Path $NuGetPackages -Force | Out-Null
    Write-Host "==> 已创建 NuGet 缓存目录 $NuGetPackages"
}

# 4) 验证
$dotnetExe = Join-Path $InstallDir 'dotnet.exe'
if (-not (Test-Path $dotnetExe)) {
    throw "安装失败：找不到 $dotnetExe"
}

Write-Host ""
Write-Host "==> 安装完成，验证结果："
& $dotnetExe --version
Write-Host "--- 已安装的 SDK ---"
& $dotnetExe --list-sdks

Write-Host ""
Write-Host "==> 当前窗口临时环境变量（不会持久化到系统）："
$env:DOTNET_ROOT = $InstallDir
$env:PATH = "$InstallDir;$env:PATH"
$env:NUGET_PACKAGES = $NuGetPackages
Write-Host "    DOTNET_ROOT    = $env:DOTNET_ROOT"
Write-Host "    NUGET_PACKAGES = $env:NUGET_PACKAGES"
Write-Host ""
Write-Host "提示：以后每次打开新窗口编译前，先执行 build\env.ps1 设置环境变量。"
