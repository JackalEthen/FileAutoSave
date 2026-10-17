<#
.SYNOPSIS
    卸载 FileAutoSave 加载项。
.DESCRIPTION
    以管理员身份运行。移除 Office 的 Addins 注册表键、反注册 COM 组件、删除程序目录。
    默认保留用户数据（%APPDATA%\FileAutoSave 下的设置、凭证、快照）。
    加 -RemoveUserData 可一并删除。
#>
#Requires -RunAsAdministrator
param(
    [switch]$RemoveUserData
)

$ErrorActionPreference = 'Stop'

$target = Join-Path $env:ProgramFiles 'FileAutoSave'
$userData = Join-Path $env:APPDATA 'FileAutoSave'

$addins = @(
    @{ App = 'Word';       Dll = 'FileAutoSave.AddIn.Word.dll';       ProgId = 'FileAutoSave.AddIn.Word.Connect' },
    @{ App = 'Excel';      Dll = 'FileAutoSave.AddIn.Excel.dll';      ProgId = 'FileAutoSave.AddIn.Excel.Connect' },
    @{ App = 'PowerPoint'; Dll = 'FileAutoSave.AddIn.PowerPoint.dll'; ProgId = 'FileAutoSave.AddIn.PowerPoint.Connect' }
)

$regasm32 = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\RegAsm.exe'
$regasm64 = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'

$hives = @(
    'HKLM:\Software\Microsoft\Office',
    'HKLM:\Software\Wow6432Node\Microsoft\Office'
)

Write-Host '1/3 移除 Office Addins 注册表键 ...'
foreach ($addin in $addins) {
    foreach ($hive in $hives) {
        $key = Join-Path $hive "$($addin.App)\Addins\$($addin.ProgId)"
        if (Test-Path $key) {
            Remove-Item -Path $key -Recurse -Force
            Write-Host "  已移除 $key"
        }
    }
}

Write-Host '2/3 反注册 COM 组件 ...'
foreach ($addin in $addins) {
    $assembly = Join-Path $target $addin.Dll
    if (Test-Path $assembly) {
        & $regasm32 $assembly /unregister /nologo | Out-Null
        & $regasm64 $assembly /unregister /nologo | Out-Null
    }
}

Write-Host '3/3 删除程序目录 ...'
if (Test-Path $target) {
    Remove-Item -Path $target -Recurse -Force
}

if ($RemoveUserData -and (Test-Path $userData)) {
    Remove-Item -Path $userData -Recurse -Force
    Write-Host "  已删除用户数据 $userData"
} else {
    Write-Host "  已保留用户数据 $userData（加 -RemoveUserData 可一并删除）"
}

Write-Host ''
Write-Host '卸载完成。' -ForegroundColor Green
