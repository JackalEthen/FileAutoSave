<#
.SYNOPSIS
    构建 FileAutoSave 的安装包。
.DESCRIPTION
    产出三个文件（都在 installer\ 下）：
      FileAutoSave-zh-CN.msi   简体中文 MSI
      FileAutoSave-en-US.msi   英文 MSI
      FileAutoSave-Setup.exe   引导程序：按用户界面语言自动选上面两份之一（无下拉）
    需要 WiX 5：dotnet tool install --global wix --version 5.*
               wix extension add -g WixToolset.UI.wixext/5.0.2
               wix extension add -g WixToolset.BootstrapperApplications.wixext/5.0.2
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$configuration = 'Release'
$installerDir = Join-Path $root 'installer'
$staging = Join-Path $installerDir 'staging'
$wxs = Join-Path $installerDir 'FileAutoSave.wxs'
$bundleWxs = Join-Path $installerDir 'bundle.wxs'
$msiZh = Join-Path $installerDir 'FileAutoSave-zh-CN.msi'
$msiEn = Join-Path $installerDir 'FileAutoSave-en-US.msi'
$setup = Join-Path $installerDir 'FileAutoSave-Setup.exe'

$projects = @(
    'FileAutoSave.Core',
    'FileAutoSave.AddIn.Word',
    'FileAutoSave.AddIn.Excel',
    'FileAutoSave.AddIn.PowerPoint'
)

Write-Host "1/5 构建 $configuration ..."
& dotnet build (Join-Path $root 'FileAutoSave.slnx') -c $configuration | Out-Null
if (-not $?) { throw '构建失败。' }

Write-Host "2/5 汇集程序集到 $staging ..."
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null

foreach ($project in $projects) {
    $bin = Join-Path $root "src\$project\bin\$configuration\net48"
    if (-not (Test-Path $bin)) { throw "找不到输出目录：$bin" }
    Get-ChildItem $bin -Filter *.dll | Copy-Item -Destination $staging -Force
}

Write-Host '3/5 打包简体中文 MSI ...'
& wix build $wxs -o $msiZh -arch x64 -ext WixToolset.UI.wixext -culture zh-CN `
    -d "StagingDir=$staging" -d "LicenseRtf=$(Join-Path $installerDir 'License.rtf')"
if (-not $?) { throw '中文 MSI 打包失败。' }
& wix msi validate $msiZh; if (-not $?) { throw '中文 MSI 校验未通过。' }

Write-Host '4/5 打包英文 MSI ...'
& wix build $wxs -o $msiEn -arch x64 -ext WixToolset.UI.wixext -culture en-US `
    -d "StagingDir=$staging" -d "LicenseRtf=$(Join-Path $installerDir 'License-en.rtf')"
if (-not $?) { throw '英文 MSI 打包失败。' }
& wix msi validate $msiEn; if (-not $?) { throw '英文 MSI 校验未通过。' }

Write-Host '5/5 打包引导程序（按系统语言自动选）...'
& wix build $bundleWxs -o $setup -arch x64 `
    -ext WixToolset.BootstrapperApplications.wixext `
    -d "MsiZh=$msiZh" -d "MsiEn=$msiEn"
if (-not $?) { throw '引导程序打包失败。' }

Write-Host ''
Write-Host '已生成：' -ForegroundColor Green
Get-Item $msiZh, $msiEn, $setup | ForEach-Object {
    Write-Host ("  {0}  ({1:N2} MB)" -f $_.Name, ($_.Length / 1MB))
}
