@echo off
REM =============================================================================
REM Скрипт просмотра логов Empyrion Dedicated Server
REM =============================================================================
REM Открывает последний лог-файл и фильтрует записи, связанные с GLEX
REM =============================================================================

setlocal enabledelayedexpansion

set "EMPYRION_ROOT=D:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival"
set "LOGS_DIR=%EMPYRION_ROOT%\Logs"

echo.
echo =========================================================================
echo Просмотр логов Empyrion (фильтр: GalacticExpansion)
echo =========================================================================
echo.

REM Проверка существования папки логов
if not exist "%LOGS_DIR%" (
    echo [ОШИБКА] Папка логов не найдена: %LOGS_DIR%
    echo.
    echo Проверьте путь к Empyrion в скрипте
    pause
    exit /b 1
)

REM Поиск последнего лог-файла
for /f "delims=" %%F in ('dir /b /o:-d "%LOGS_DIR%\*_Current.log" 2^>nul') do (
    set "LATEST_LOG=%%F"
    goto :found
)

echo [ОШИБКА] Лог-файлы не найдены в папке: %LOGS_DIR%
echo.
echo Возможно, сервер еще не запускался
pause
exit /b 1

:found
set "LOG_PATH=%LOGS_DIR%\%LATEST_LOG%"

echo Последний лог-файл: %LATEST_LOG%
echo Путь: %LOG_PATH%
echo.
echo -------------------------------------------------------------------------
echo Записи, связанные с GalacticExpansion:
echo -------------------------------------------------------------------------
echo.

REM Проверка наличия PowerShell для лучшего вывода
where powershell >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    REM Использование PowerShell для цветного вывода
    powershell -NoProfile -Command ^
        "Get-Content '%LOG_PATH%' | Where-Object { $_ -match 'GalacticExpansion|GLEX' } | ForEach-Object { if ($_ -match 'ERROR|EXCEPTION') { Write-Host $_ -ForegroundColor Red } elseif ($_ -match 'WARNING|WARN') { Write-Host $_ -ForegroundColor Yellow } elseif ($_ -match 'INFO') { Write-Host $_ -ForegroundColor Green } else { Write-Host $_ } }"
) else (
    REM Fallback на findstr
    findstr /I /C:"GalacticExpansion" /C:"GLEX" "%LOG_PATH%"
)

echo.
echo -------------------------------------------------------------------------
echo.

REM Спросить, открыть ли полный лог
echo Открыть полный лог-файл в Блокноте? (Y/N)
set /p "OPEN_FULL="

if /i "%OPEN_FULL%"=="Y" (
    start notepad "%LOG_PATH%"
)

echo.
echo =========================================================================
echo.
pause
exit /b 0
