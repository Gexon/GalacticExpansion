@echo off
chcp 65001 >nul
REM =============================================================================
REM Скрипт просмотра логов GalacticExpansion
REM =============================================================================
REM Показывает логи из двух источников:
REM 1. Логи мода из Content/Mods/GalacticExpansion/Logs/ (основные логи)
REM 2. Записи мода из логов Empyrion Dedicated Server (fallback)
REM 
REM Также копирует новые логи мода в папку проекта\logs для анализа
REM =============================================================================

setlocal enabledelayedexpansion

set "EMPYRION_ROOT=D:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival"
set "MOD_LOGS_DIR=%EMPYRION_ROOT%\Content\Mods\GalacticExpansion\Logs"
set "SERVER_LOGS_DIR=%EMPYRION_ROOT%\Logs"
set "PROJECT_LOGS_DIR=E:\for_game\Empyrion\GalacticExpansion\logs"

echo.
echo =========================================================================
echo Просмотр логов GalacticExpansion
echo =========================================================================
echo.

REM ---------------------------------------------------------------------------
REM Часть 1: Логи мода (основной источник)
REM ---------------------------------------------------------------------------
echo [1] Проверка логов мода...
echo     Папка: %MOD_LOGS_DIR%
echo.

if exist "%MOD_LOGS_DIR%" (
    REM Ищем последний лог-файл мода (с новым форматом: GLEX_2026-02-02_143025.log)
    set "MOD_LOG="
    for /f "delims=" %%F in ('dir /b /o:-d "%MOD_LOGS_DIR%\GLEX_*.log" 2^>nul ^| findstr /v "errors"') do (
        set "MOD_LOG=%%F"
        goto :mod_found
    )
    
    echo [!] Лог-файлы мода не найдены в папке логов
    echo     Возможно, мод еще не запускался после последнего обновления
    echo.
    goto :check_server_logs
    
    :mod_found
    set "MOD_LOG_PATH=%MOD_LOGS_DIR%\!MOD_LOG!"
    echo [OK] Найден лог мода: !MOD_LOG!
    echo     Путь: !MOD_LOG_PATH!
    echo.
    echo -------------------------------------------------------------------------
    echo Содержимое лога мода ^(последние 500 строк^):
    echo -------------------------------------------------------------------------
    echo.
    
    REM Используем PowerShell для вывода последних 50 строк с цветной подсветкой
    powershell -NoProfile -Command "Get-Content '!MOD_LOG_PATH!' -Tail 500 | ForEach-Object { if ($_ -match 'ERROR|FATAL|EXCEPTION') { Write-Host $_ -ForegroundColor Red } elseif ($_ -match 'WARNING|WARN') { Write-Host $_ -ForegroundColor Yellow } elseif ($_ -match 'INFO') { Write-Host $_ -ForegroundColor Green } elseif ($_ -match 'DEBUG') { Write-Host $_ -ForegroundColor Cyan } else { Write-Host $_ } }"
            
    goto :end
) else (
    echo [!] Папка логов мода не существует: %MOD_LOGS_DIR%
    echo     Логи будут создаться после первого запуска мода
    echo.
)

REM ---------------------------------------------------------------------------
REM Часть 2: Логи из Empyrion Dedicated Server (fallback)
REM ---------------------------------------------------------------------------
:check_server_logs
echo [2] Проверка логов Empyrion Dedicated Server...
echo     Папка: %SERVER_LOGS_DIR%
echo.

if not exist "%SERVER_LOGS_DIR%" (
    echo [ОШИБКА] Папка логов сервера не найдена: %SERVER_LOGS_DIR%
    echo.
    echo Проверьте путь к Empyrion в скрипте
    echo.
    goto :end
)

REM Ищем последний Dedicated лог-файл
for /f "delims=" %%D in ('dir /b /s "%SERVER_LOGS_DIR%\Dedicated*.log" 2^>nul') do (
    set "TEMP_LOG=%%D"
)

if not defined TEMP_LOG (
    echo [ОШИБКА] Лог-файлы Dedicated Server не найдены
    echo.
    echo Возможно, сервер еще не запускался
    echo.
    goto :end
)

REM Находим самый свежий файл
set "SERVER_LOG="
for /f "delims=" %%F in ('dir /b /o:-d "%SERVER_LOGS_DIR%\*\Dedicated*.log" 2^>nul') do (
    for /f "delims=" %%P in ('dir /b /s /o:-d "%SERVER_LOGS_DIR%\Dedicated*.log" 2^>nul') do (
        set "SERVER_LOG=%%P"
        goto :server_found
    )
)

:server_found
if not defined SERVER_LOG (
    echo [ОШИБКА] Не удалось найти логи Dedicated Server
    echo.
    goto :end
)

echo [OK] Найден лог сервера: 
echo     %SERVER_LOG%
echo.
echo -------------------------------------------------------------------------
echo Записи GalacticExpansion из лога сервера:
echo -------------------------------------------------------------------------
echo.

REM Используем PowerShell для фильтрации и цветного вывода
powershell -NoProfile -Command ^
    "Get-Content '%SERVER_LOG%' | Where-Object { $_ -match 'GalacticExpansion|GLEX' } | ForEach-Object { if ($_ -match 'ERROR|FATAL|EXCEPTION') { Write-Host $_ -ForegroundColor Red } elseif ($_ -match 'WARNING|WARN') { Write-Host $_ -ForegroundColor Yellow } elseif ($_ -match 'INFO') { Write-Host $_ -ForegroundColor Green } elseif ($_ -match 'DEBUG') { Write-Host $_ -ForegroundColor Cyan } else { Write-Host $_ } }"

echo.
echo -------------------------------------------------------------------------
echo.

:end
echo.

REM ---------------------------------------------------------------------------
REM Часть 3: Копирование логов в папку проекта
REM ---------------------------------------------------------------------------
echo [3] Копирование логов в папку проекта...
echo     Источник: %MOD_LOGS_DIR%
echo     Назначение: %PROJECT_LOGS_DIR%
echo.

REM Создаем папку logs в проекте, если не существует
if not exist "%PROJECT_LOGS_DIR%" (
    mkdir "%PROJECT_LOGS_DIR%"
    echo     [OK] Папка создана
)

REM Копируем файлы логов с перезаписью устаревших
if exist "%MOD_LOGS_DIR%\*.log" (
    set "COPIED_COUNT=0"
    set "UPDATED_COUNT=0"
    for %%F in ("%MOD_LOGS_DIR%\*.log") do (
        set "NEED_COPY=0"
        
        REM Проверяем, существует ли файл в проекте
        if not exist "%PROJECT_LOGS_DIR%\%%~nxF" (
            set "NEED_COPY=1"
        ) else (
            REM Сравниваем даты модификации через PowerShell
            for /f "usebackq" %%T in (`powershell -NoProfile -Command "if ((Get-Item '%%F').LastWriteTime -gt (Get-Item '%PROJECT_LOGS_DIR%\%%~nxF').LastWriteTime) { 'NEWER' } else { 'OLDER' }"`) do (
                if "%%T"=="NEWER" (
                    set "NEED_COPY=2"
                )
            )
        )
        
        REM Копируем, если нужно
        if !NEED_COPY! GTR 0 (
            copy /Y "%%F" "%PROJECT_LOGS_DIR%\" >nul 2>&1
            if !ERRORLEVEL! EQU 0 (
                if !NEED_COPY! EQU 1 (
                    set /a COPIED_COUNT+=1
                    echo     [+] Новый файл: %%~nxF
                ) else (
                    set /a UPDATED_COUNT+=1
                    echo     [*] Обновлён: %%~nxF
                )
            )
        )
    )
    
    if !COPIED_COUNT! EQU 0 if !UPDATED_COUNT! EQU 0 (
        echo     [OK] Все логи актуальны, обновление не требуется
    ) else (
        if !COPIED_COUNT! GTR 0 echo     [OK] Скопировано новых файлов: !COPIED_COUNT!
        if !UPDATED_COUNT! GTR 0 echo     [OK] Обновлено файлов: !UPDATED_COUNT!
    )
) else (
    echo     [!] Логи мода не найдены для копирования
)

echo.
echo =========================================================================
exit /b 0
