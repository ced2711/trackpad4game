@echo off
rem Builds trackpad4game.exe with the C# compiler that ships with Windows (.NET Framework 4.x). Nothing to install.
setlocal
cd /d "%~dp0"
set CSC="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set COMMON=/nologo /target:winexe /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll
if not exist obj mkdir obj

rem Pass 1: build once to render the icon from the same drawing code the tray uses.
%CSC% %COMMON% /out:obj\icon-gen.exe src\*.cs || exit /b 1
start /wait "" obj\icon-gen.exe --write-icon obj\trackpad4game.ico
del obj\icon-gen.exe

rem Pass 2: the real exe, with the icon and a per-monitor DPI-aware manifest.
%CSC% %COMMON% /out:trackpad4game.exe /win32icon:obj\trackpad4game.ico /win32manifest:src\app.manifest src\*.cs || exit /b 1
echo Built %~dp0trackpad4game.exe
