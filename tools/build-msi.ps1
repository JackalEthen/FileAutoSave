<#
.SYNOPSIS
    构建 FileAutoSave 的 MSI。
.DESCRIPTION
    1) dotnet build Release
    2) 把 4 个工程的输出 DLL 与注册脚本汇集到 installer\staging
    3) 用 WiX 打包成 installer\FileAutoSave.msi
    需要预先安装 WiX：dotnet tool install --global wix --version 4.*
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$configuration = 'Release'
$installerDir = Join-Path $root 'installer'
$staging = Join-Path $installerDir 'staging'
$wxs = Join-Path $installerDir 'FileAutoSave.wxs'
$msi = Join-Path $installerDir 'FileAutoSave.msi'

$projects = @(
    'FileAutoSave.Core',
    'FileAutoSave.AddIn.Word',
    'FileAutoSave.AddIn.Excel',
    'FileAutoSave.AddIn.PowerPoint'
)

Write-Host "1/3 构建 $configuration ..."
& dotnet build (Join-Path $root 'FileAutoSave.slnx') -c $configuration | Out-Null
if (-not $?) { throw '构建失败。' }

Write-Host "2/3 汇集程序集到 $staging ..."
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null

foreach ($project in $projects) {
    $bin = Join-Path $root "src\$project\bin\$configuration\net48"
    if (-not (Test-Path $bin)) { throw "找不到输出目录：$bin" }
    Get-ChildItem $bin -Filter *.dll | Copy-Item -Destination $staging -Force
}

Write-Host "3/4 打包 MSI ..."
& wix build $wxs -o $msi -arch x64 -ext WixToolset.UI.wixext `
    -d "StagingDir=$staging" -d "LicenseRtf=$(Join-Path $installerDir 'License.rtf')"
if (-not $?) { throw 'wix 打包失败。' }

Write-Host "4/4 校验 MSI ..."
& wix msi validate $msi
if (-not $?) { throw 'MSI 校验未通过。' }

Write-Host ''
Write-Host "已生成：$msi" -ForegroundColor Green
