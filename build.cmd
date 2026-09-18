@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo Nu gasesc compilatorul C# la "%CSC%".
    exit /b 1
)

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /utf8output ^
    /out:"%~dp0CursorSelector.exe" ^
    /win32icon:"%~dp0app.ico" ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.IO.Compression.dll ^
    /reference:System.IO.Compression.FileSystem.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    /reference:System.Web.Extensions.dll ^
    /reference:Microsoft.VisualBasic.dll ^
    "%~dp0src\CursorSelector.cs"

if errorlevel 1 (
    echo.
    echo BUILD ESUAT
    exit /b 1
)
echo Gata: %~dp0CursorSelector.exe
