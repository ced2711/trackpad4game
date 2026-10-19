@echo off
rem Builds trackpad4game and its tests, runs the unit tests, then the end-to-end hook test
rem against a throwaway copy in tests\run.
setlocal
cd /d "%~dp0.."
set CSC="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
call "%~dp0..\build.cmd" || exit /b 1
if not exist tests\run mkdir tests\run

%CSC% /nologo /codepage:65001 /out:tests\run\UnitTests.exe /reference:System.Windows.Forms.dll ^
  tests\UnitTests.cs src\Mapper.cs src\Settings.cs || exit /b 1
tests\run\UnitTests.exe || exit /b 1

copy /y trackpad4game.exe tests\run\ >nul
%CSC% /nologo /platform:x64 /out:tests\run\HookTest.exe /reference:System.Windows.Forms.dll tests\HookTest.cs || exit /b 1
tests\run\HookTest.exe tests\run\trackpad4game.exe
