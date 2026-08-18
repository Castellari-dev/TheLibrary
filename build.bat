@echo off
REM ============================================================
REM  The Library - gera um unico .exe para Windows x64
REM  Requer: .NET 8 SDK (https://dotnet.microsoft.com/download)
REM ============================================================

echo.
echo == Restaurando pacotes ==
dotnet restore TheLibrary.csproj
if errorlevel 1 goto erro

echo.
echo == Publicando (self-contained, arquivo unico) ==
dotnet publish TheLibrary.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o publish
if errorlevel 1 goto erro

echo.
echo ============================================================
echo  Pronto: publish\TheLibrary.exe
echo ============================================================
goto fim

:erro
echo.
echo *** A compilacao falhou. Verifique as mensagens acima. ***
exit /b 1

:fim
pause
