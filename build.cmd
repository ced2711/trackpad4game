@echo off
rem Builds trackpad4game.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /out:trackpad4game.exe ^
  /reference:System.Windows.Forms.dll /reference:System.Drawing.dll Program.cs
