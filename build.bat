@echo off
chcp 65001 >nul
setlocal

REM ============================================================
REM  MyAutoDNS - publication des trois programmes et de l'installeur
REM
REM  1. arrete les programmes en cours
REM  2. publie Service, Tray et UI dans publish\Serveur
REM  3. compile l'installeur Inno Setup
REM
REM  Resultat : Installers\MyAutoDNS-<version>.exe
REM
REM  Usage : build.bat [--auto]
REM          --auto : pas de pause, pas d'ouverture du dossier
REM ============================================================

for /F "delims=" %%A in ('echo prompt $E^| cmd') do set "ESC=%%A"
set "VERT=%ESC%[92m"
set "ROUGE=%ESC%[91m"
set "JAUNE=%ESC%[93m"
set "CYAN=%ESC%[96m"
set "RAZ=%ESC%[0m"

set "ROOT=%~dp0"
set "PUB=%ROOT%publish\Serveur"
set "DEST_SERVICE=%PUB%\ProgramData\MyAutoDNS"
set "DEST_PROGRAM=%PUB%\ProgramFiles\MyAutoDNS"
set "INSTALLERS=%ROOT%Installers"
set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

echo %CYAN%============================================%RAZ%
echo %CYAN%  MyAutoDNS - build%RAZ%
echo %CYAN%============================================%RAZ%
echo.

echo %JAUNE%  [INFO]%RAZ% Arret des programmes MyAutoDNS...
schtasks /End /TN "MyAutoDNS Tray" >nul 2>&1
schtasks /End /TN "MyAutoDNS Service" >nul 2>&1
taskkill /IM MyAutoDNS.UI.exe /F >nul 2>&1
taskkill /IM MyAutoDNS.Tray.exe /F >nul 2>&1
taskkill /IM MyAutoDNS.Service.exe /F >nul 2>&1
echo   %VERT%[OK]%RAZ%   programmes arretes ou deja fermes
echo.

echo %JAUNE%  [INFO]%RAZ% Nettoyage de publish\Serveur...
if exist "%PUB%" rmdir /s /q "%PUB%"
echo.

call :Publier "MyAutoDNS.Service\MyAutoDNS.Service.csproj" "Service" "%DEST_SERVICE%"
if errorlevel 1 goto :Echec

call :Publier "MyAutoDNS.Tray\MyAutoDNS.Tray.csproj" "Tray" "%DEST_PROGRAM%"
if errorlevel 1 goto :Echec

call :Publier "MyAutoDNS.UI\MyAutoDNS.UI.csproj" "UI" "%DEST_PROGRAM%"
if errorlevel 1 goto :Echec

echo %JAUNE%  [INFO]%RAZ% Compilation de l'installeur...
echo.

if not exist "%ISCC%" (
    echo   %ROUGE%[ERREUR]%RAZ% Inno Setup 6 est introuvable.
    echo   Installez-le depuis https://jrsoftware.org/isdl.php
    goto :Echec
)

if not exist "%INSTALLERS%" mkdir "%INSTALLERS%"

REM On retire les versions precedentes, pour ne pas laisser plusieurs
REM installeurs cote a cote et semer le doute sur celui a livrer.
if exist "%INSTALLERS%\MyAutoDNS-*.exe" del /q "%INSTALLERS%\MyAutoDNS-*.exe"

"%ISCC%" /Q "%ROOT%inno\MyAutoDNS.iss"
if errorlevel 1 (
    echo   %ROUGE%[ERREUR]%RAZ% Compilation de l'installeur impossible.
    goto :Echec
)

for %%F in ("%INSTALLERS%\MyAutoDNS-*.exe") do echo   %VERT%[OK]%RAZ%   %%~nxF

echo.
echo %VERT%============================================%RAZ%
echo %VERT%  BUILD TERMINE%RAZ%
echo %VERT%============================================%RAZ%
echo.
echo   Installeur dans : %INSTALLERS%
goto :Fin

:Echec
echo.
echo %ROUGE%============================================%RAZ%
echo %ROUGE%  ECHEC DU BUILD%RAZ%
echo %ROUGE%============================================%RAZ%
if /I not "%~1"=="--auto" pause
exit /b 1

:Fin
if /I not "%~1"=="--auto" start "" "%INSTALLERS%"
if /I not "%~1"=="--auto" pause
exit /b 0

REM ============================================================
REM  Publication d'un projet
REM  %1 = chemin du .csproj, relatif a la racine
REM  %2 = nom affiche
REM  %3 = dossier de destination
REM ============================================================
:Publier
set "PROJET=%~1"
set "NOM=%~2"
set "CIBLE=%~3"
echo   [....] %NOM%
dotnet publish "%ROOT%%PROJET%" -c Release -r win-x64 --self-contained false -p:PlatformTarget=x64 -o "%CIBLE%" --nologo -v quiet
if errorlevel 1 (
    echo   %ROUGE%[ERREUR]%RAZ% publication de %NOM% impossible
    exit /b 1
)
echo   %VERT%[OK]%RAZ%   %NOM%
echo.
exit /b 0