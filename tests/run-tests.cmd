@echo off
rem Builds trackpad4game and the hook test, then runs the test against a throwaway copy in tests\run.
cd /d "%~dp0.."
call "%~dp0..\build.cmd" || exit /b 1
if not exist tests\run mkdir tests\run
copy /y trackpad4game.exe tests\run\ >nul
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /platform:x64 /out:tests\run\HookTest.exe ^
  /reference:System.Windows.Forms.dll tests\HookTest.cs || exit /b 1
tests\run\HookTest.exe tests\run\trackpad4game.exe
