# Compila o Ranger.exe com o compilador C# que já vem no Windows (.NET Framework 4.x).
$ErrorActionPreference = 'Stop'
$fw  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $fw 'WPF'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here 'Ranger.exe'
$ico = Join-Path $here 'ranger.ico'

if (-not (Test-Path $ico)) { & (Join-Path $here 'make-icon.ps1') | Out-Null }

& "$fw\csc.exe" /nologo /target:winexe /optimize+ /platform:x64 /out:"$out" /win32icon:"$ico" `
    /r:"$wpf\PresentationFramework.dll" /r:"$wpf\PresentationCore.dll" /r:"$wpf\WindowsBase.dll" /r:"$fw\System.Xaml.dll" `
    (Join-Path $here 'src\AssemblyInfo.cs') (Join-Path $here 'src\Devices.cs') (Join-Path $here 'src\Widget.cs') (Join-Path $here 'src\SettingsWindow.cs')
if ($LASTEXITCODE -ne 0) { throw "falha na compilação" }
"ok: $out"
