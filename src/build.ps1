# Сборка BUL$ Opti Free.  -Preview: без манифеста (для отрисовки экранов), иначе релиз с манифестом asInvoker
param([switch]$Preview, [string]$Out = "")
$ErrorActionPreference = "Stop"
$fw  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
Set-Location $PSScriptRoot
if ($Out -eq "") { $Out = $(if ($Preview) { "preview.exe" } else { "BulsOptiFree.exe" }) }
$args2 = @("/nologo", "/target:winexe", "/optimize+", "/debug-", "/highentropyva+", "/platform:anycpu", "/out:$Out", "/win32icon:icon.ico",
  "/resource:bull.png,BulsOpti.bull.png", "/resource:wall.png,BulsOpti.wall.png",
  "/reference:$wpf\PresentationFramework.dll", "/reference:$wpf\PresentationCore.dll", "/reference:$wpf\WindowsBase.dll",
  "/reference:$fw\System.Xaml.dll", "/reference:System.dll", "/reference:System.Core.dll",
  "Engine.cs", "Clean.cs", "Loc.cs", "Ui.cs", "App.cs", "AssemblyInfo.cs")
if (-not $Preview) { $args2 = @("/win32manifest:app.manifest") + $args2 }
& "$fw\csc.exe" @args2
if ($LASTEXITCODE -ne 0) { Write-Output "BUILD FAILED"; exit 1 }
Write-Output ("BUILD OK: " + (Get-Item $Out).FullName + "  " + (Get-Item $Out).Length + " bytes")
