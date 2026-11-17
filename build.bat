@echo off
rem Compila o Sintonia usando o compilador C# que ja vem no Windows (.NET Framework 4.x).
rem Nao precisa instalar nada.

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo Compilador C# nao encontrado. Instale o .NET Framework 4.8.
    exit /b 1
)

cd /d "%~dp0"
set ICO=
if exist Sintonia.ico set ICO=/win32icon:Sintonia.ico
"%CSC%" /nologo /target:winexe /optimize /platform:anycpu %ICO% /out:Sintonia.exe Sintonia.cs
if errorlevel 1 (
    echo.
    echo Erro ao compilar.
    exit /b 1
)
echo.
echo Pronto: Sintonia.exe
