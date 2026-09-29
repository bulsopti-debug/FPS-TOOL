@echo off
REM Пересборка BULS Opti Optimizer.exe (нужен только Windows, всё уже в системе)
setlocal
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set WPF=%FW%\WPF
cd /d "%~dp0"
"%FW%\csc.exe" /nologo /target:winexe "/out:BULS Opti Optimizer.exe" /win32manifest:app.manifest /win32icon:icon.ico "/resource:bull.png,BulsOpti.bull.png" "/resource:wall.png,BulsOpti.wall.png" "/reference:%WPF%\PresentationFramework.dll" "/reference:%WPF%\PresentationCore.dll" "/reference:%WPF%\WindowsBase.dll" "/reference:%FW%\System.Xaml.dll" /reference:System.dll /reference:System.Core.dll Program.cs App.cs AssemblyInfo.cs
if %errorlevel%==0 (echo BUILD OK) else (echo BUILD FAILED %errorlevel%)
pause
