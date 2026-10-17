<#
.SYNOPSIS
    安装 FileAutoSave 加载项到 Word / Excel / PowerPoint。
.DESCRIPTION
    以管理员身份运行。会构建 Release、把程序集装到 %ProgramFiles%\FileAutoSave，
    用 32 位与 64 位 RegAsm 各注册一次，并在 64 位与 Wow6432Node 两处写 Office 的 Addins 键。
#>
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$configuration = 'Release'
$target = Join-Path $env:ProgramFiles 'FileAutoSave'

$addins = @(
    @{ App = 'Word';       Dll = 'FileAutoSave.AddIn.Word.dll';       ProgId = 'FileAutoSave.AddIn.Word.Connect' },
    @{ App = 'Excel';      Dll = 'FileAutoSave.AddIn.Excel.dll';      ProgId = 'FileAutoSave.AddIn.Excel.Connect' },
    @{ App = 'PowerPoint'; Dll = 'FileAutoSave.AddIn.PowerPoint.dll'; ProgId = 'FileAutoSave.AddIn.PowerPoint.Connect' }
)

$stagingProjects = @(
    'FileAutoSave.Core',
    'FileAutoSave.AddIn.Word',
    'FileAutoSave.AddIn.Excel',
    'FileAutoSave.AddIn.PowerPoint'
)

$regasm32 = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\RegAsm.exe'
$regasm64 = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'

Write-Host "1/4 构建 $configuration ..."
& dotnet build (Join-Path $root 'FileAutoSave.slnx') -c $configuration | Out-Null
if (-not $?) { throw '构建失败。' }

Write-Host "2/4 部署到 $target ..."
New-Item -ItemType Directory -Force -Path $target | Out-Null
foreach ($project in $stagingProjects) {
    $bin = Join-Path $root "src\$project\bin\$configuration\net48"
    if (-not (Test-Path $bin)) { throw "找不到输出目录：$bin" }
    Copy-Item (Join-Path $bin '*') -Destination $target -Recurse -Force
}

Write-Host '3/4 注册 COM 组件（32 位 + 64 位）...'
foreach ($addin in $addins) {
    $assembly = Join-Path $target $addin.Dll
    & $regasm32 $assembly /codebase /nologo | Out-Null
    & $regasm64 $assembly /codebase /nologo | Out-Null
}

Write-Host '4/4 写入 Office Addins 注册表键 ...'
$hives = @(
    'HKLM:\Software\Microsoft\Office',
    'HKLM:\Software\Wow6432Node\Microsoft\Office'
)

foreach ($addin in $addins) {
    foreach ($hive in $hives) {
        $key = Join-Path $hive "$($addin.App)\Addins\$($addin.ProgId)"
        New-Item -Path $key -Force | Out-Null
        New-ItemProperty -Path $key -Name 'FriendlyName' -Value 'FileAutoSave'          -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $key -Name 'Description'  -Value '文件自动保存'          -PropertyType String -Force | Out-Null
        # 3 = 随宿主启动并连接。不要用 16——它不含"连接"位，功能区会渲染但回调失效。
        New-ItemProperty -Path $key -Name 'LoadBehavior' -Value 3                       -PropertyType DWord  -Force | Out-Null
    }

    Write-Host "  $($addin.App) -> $($hive)\...\$($addin.ProgId)"
}

Write-Host ''
Write-Host '安装完成。启动 Word / Excel / PowerPoint，在「开始」选项卡可见「文件自动保存」组。' -ForegroundColor Green
Write-Host '卸载请运行 tools\uninstall.ps1。'
