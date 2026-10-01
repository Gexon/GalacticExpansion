@echo off
chcp 65001 >nul
REM =============================================================================
REM Скрипт обновления мода GalacticExpansion после успешной сборки
REM =============================================================================
REM Автоматически создает бэкапы, копирует новые файлы в папку мода Empyrion,
REM удаляет старые логи мода из папки игры и сохраняет пользовательские
REM настройки (Configuration.json и state.json)
REM
REM Использование:
REM   deploy_mod.cmd [build_config]
REM   build_config - конфигурация сборки (Debug или Release), по умолчанию Release
REM
REM Пути:
REM   Проект: E:\for_game\Empyrion\GalacticExpansion
REM   Путь к игре: загружается из config\Configuration.json (EmpyrionPath)
REM   Фаллбек: C:\Program Files (x86)\Steam\steamapps\common\Empyrion - Galactic Survival
REM =============================================================================

setlocal enabledelayedexpansion

REM ---------------------------------------------------------------------------
REM Конфигурация путей
REM ---------------------------------------------------------------------------
set "PROJECT_DIR=E:\for_game\Empyrion\GalacticExpansion"

REM Путь по умолчанию к игре (фаллбек)
set "EMPYRION_ROOT=C:\Program Files (x86)\Steam\steamapps\common\Empyrion - Galactic Survival"
set "CONFIG_FILE=%PROJECT_DIR%\config\Configuration.json"

REM Попытка загрузить путь из конфигурации
if exist "%CONFIG_FILE%" (
    REM Ищем строку "EmpyrionPath" в JSON и извлекаем значение
    for /f "usebackq tokens=* delims=" %%A in (`findstr /C:"EmpyrionPath" "%CONFIG_FILE%"`) do (
        set "JSON_LINE=%%A"
        REM Убираем всё до первых кавычек значения (после ":)
        set "JSON_LINE=!JSON_LINE:*: "=!"
        REM Убираем закрывающие кавычки и запятую
        set "JSON_LINE=!JSON_LINE:",=!"
        set "JSON_LINE=!JSON_LINE:"=!"
        REM Заменяем \\ на \
        set "JSON_LINE=!JSON_LINE:\\=\!"
        REM Убираем пробелы в начале и конце
        for /f "tokens=*" %%B in ("!JSON_LINE!") do set "TEMP_PATH=%%B"
        if not "!TEMP_PATH!"=="" (
            set "EMPYRION_ROOT=!TEMP_PATH!"
            echo [INFO] Путь к игре загружен из конфигурации
        )
    )
)

REM Целевая папка мода (БЕЗ DedicatedServer - правильный путь!)
set "MOD_TARGET=!EMPYRION_ROOT!\Content\Mods\GalacticExpansion"
set "CONFIG_DIR=%PROJECT_DIR%\config"

REM Конфигурация сборки (Debug или Release)
set "BUILD_CONFIG=%~1"
if "%BUILD_CONFIG%"=="" set "BUILD_CONFIG=Release"

REM Папка сборки (.NET SDK структура)
set "BUILD_DIR=%PROJECT_DIR%\src\GalacticExpansion\bin\%BUILD_CONFIG%\net48"

REM Временная метка для бэкапов
set "TIMESTAMP=%date:~-4%%date:~3,2%%date:~0,2%_%time:~0,2%%time:~3,2%%time:~6,2%"
set "TIMESTAMP=%TIMESTAMP: =0%"

echo [1/9] Проверка путей...

REM Проверка существования папки проекта
if not exist "%PROJECT_DIR%" (
    echo [ОШИБКА] Папка проекта не найдена: %PROJECT_DIR%
    exit /b 1
)

REM Проверка существования папки сборки
if not exist "%BUILD_DIR%" (
    echo [ОШИБКА] Папка сборки не найдена: %BUILD_DIR%
    echo Убедитесь, что проект собран в конфигурации %BUILD_CONFIG%
    exit /b 1
)

REM Проверка существования основного файла мода
if not exist "%BUILD_DIR%\GalacticExpansion.dll" (
    echo [ОШИБКА] Файл GalacticExpansion.dll не найден в %BUILD_DIR%
    echo Выполните сборку проекта перед запуском этого скрипта
    exit /b 1
)

echo [OK] Все пути найдены

REM ---------------------------------------------------------------------------
REM Создание целевой папки мода, если не существует
REM ---------------------------------------------------------------------------
echo.
echo [2/9] Подготовка целевой папки...

if not exist "!MOD_TARGET!" (
    echo Создание новой папки мода: !MOD_TARGET!
    mkdir "!MOD_TARGET!"
    if errorlevel 1 (
        echo [ОШИБКА] Не удалось создать папку мода
        exit /b 1
    )
) else (
    echo Папка мода уже существует: !MOD_TARGET!
)

REM ---------------------------------------------------------------------------
REM Создание бэкапов существующих файлов (следуя Operations Runbook 5.1)
REM ---------------------------------------------------------------------------
echo.
echo [3/9] Создание бэкапов...

REM Создание папки для бэкапов, если не существует
if not exist "!MOD_TARGET!\backups" mkdir "!MOD_TARGET!\backups"

REM Список файлов для бэкапа
set "BACKUP_FILES=GalacticExpansion.dll GalacticExpansion.Core.dll GalacticExpansion.Models.dll NLog.dll Newtonsoft.Json.dll NLog.config Configuration.json state.json"

REM Создаем временную папку для сбора файлов перед архивацией
set "TEMP_BACKUP_DIR=!MOD_TARGET!\backups\temp_%TIMESTAMP%"
if not exist "!TEMP_BACKUP_DIR!" mkdir "!TEMP_BACKUP_DIR!"

REM Копируем файлы во временную папку
set "FILES_TO_BACKUP=0"
for %%F in (%BACKUP_FILES%) do (
    if exist "!MOD_TARGET!\%%F" (
        echo Подготовка к бэкапу: %%F
        copy /Y "!MOD_TARGET!\%%F" "!TEMP_BACKUP_DIR!\%%F" >nul
        if not errorlevel 1 (
            set /a FILES_TO_BACKUP+=1
        ) else (
            echo [ПРЕДУПРЕЖДЕНИЕ] Не удалось скопировать %%F
        )
    )
)

REM Создание архива всех файлов одним ZIP-файлом
if !FILES_TO_BACKUP! GTR 0 (
    echo Архивация !FILES_TO_BACKUP! файлов в backup_%TIMESTAMP%.zip...
    
    REM Используем .NET класс ZipFile для создания архива (работает на всех версиях Windows)
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; try { [System.IO.Compression.ZipFile]::CreateFromDirectory('!TEMP_BACKUP_DIR!', '!MOD_TARGET!\backups\backup_%TIMESTAMP%.zip'); Write-Host '[OK] Архив создан: backup_%TIMESTAMP%.zip' } catch { Write-Host '[ОШИБКА] Не удалось создать архив: ' + $_.Exception.Message; exit 1 }"
    
    if errorlevel 1 (
        echo [ПРЕДУПРЕЖДЕНИЕ] Архивация не удалась, файлы останутся во временной папке
        echo Временная папка: !TEMP_BACKUP_DIR!
    ) else (
        REM Удаляем временную папку после успешной архивации
        if exist "!TEMP_BACKUP_DIR!" (
            rmdir /S /Q "!TEMP_BACKUP_DIR!" >nul 2>&1
        )
    )
) else (
    echo [ПРЕДУПРЕЖДЕНИЕ] Нет файлов для бэкапа
    REM Удаляем пустую временную папку
    if exist "!TEMP_BACKUP_DIR!" (
        rmdir /Q "!TEMP_BACKUP_DIR!" >nul 2>&1
    )
)

echo [OK] Бэкапы созданы

REM ---------------------------------------------------------------------------
REM Копирование файлов мода (НЕ заменяя конфигурацию и state.json)
REM ---------------------------------------------------------------------------
echo.
echo [4/9] Копирование файлов мода...

REM Счётчики для итоговой сводки
set "COPIED_COUNT=0"
set "FAILED_COUNT=0"

REM Копирование основной DLL
echo Копирование: GalacticExpansion.dll
copy /Y "%BUILD_DIR%\GalacticExpansion.dll" "!MOD_TARGET!\" >nul
if errorlevel 1 (
    echo [ОШИБКА] Не удалось скопировать GalacticExpansion.dll
    set /a FAILED_COUNT+=1
    exit /b 1
) else (
    set /a COPIED_COUNT+=1
)

REM Копирование PDB (отладочная информация) для Debug-сборки
if "%BUILD_CONFIG%"=="Debug" (
    if exist "%BUILD_DIR%\GalacticExpansion.pdb" (
        echo Копирование: GalacticExpansion.pdb (отладочные символы)
        copy /Y "%BUILD_DIR%\GalacticExpansion.pdb" "!MOD_TARGET!\" >nul
        if not errorlevel 1 set /a COPIED_COUNT+=1
    )
)

REM Копирование зависимостей
echo Копирование зависимостей...
if exist "%BUILD_DIR%\GalacticExpansion.Core.dll" (
    echo   - GalacticExpansion.Core.dll
    copy /Y "%BUILD_DIR%\GalacticExpansion.Core.dll" "!MOD_TARGET!\" >nul
    if not errorlevel 1 set /a COPIED_COUNT+=1
)
if exist "%BUILD_DIR%\GalacticExpansion.Models.dll" (
    echo   - GalacticExpansion.Models.dll
    copy /Y "%BUILD_DIR%\GalacticExpansion.Models.dll" "!MOD_TARGET!\" >nul
    if not errorlevel 1 set /a COPIED_COUNT+=1
)
if exist "%BUILD_DIR%\NLog.dll" (
    echo   - NLog.dll
    copy /Y "%BUILD_DIR%\NLog.dll" "!MOD_TARGET!\" >nul
    if not errorlevel 1 set /a COPIED_COUNT+=1
)
if exist "%BUILD_DIR%\Newtonsoft.Json.dll" (
    echo   - Newtonsoft.Json.dll
    copy /Y "%BUILD_DIR%\Newtonsoft.Json.dll" "!MOD_TARGET!\" >nul
    if not errorlevel 1 set /a COPIED_COUNT+=1
)
if exist "%BUILD_DIR%\NLog.config" (
    echo   - NLog.config
    copy /Y "%BUILD_DIR%\NLog.config" "!MOD_TARGET!\" >nul
    if not errorlevel 1 set /a COPIED_COUNT+=1
)

echo [OK] Файлы мода скопированы

REM ---------------------------------------------------------------------------
REM Копирование конфигурации по умолчанию (только если не существует)
REM ---------------------------------------------------------------------------
echo.
echo [5/9] Проверка конфигурации...

REM Проверяем наличие файла в целевой папке
if exist "!MOD_TARGET!\Configuration.json" (
    echo Сохранение существующей конфигурации ^(НЕ перезаписываем Configuration.json^)
) else (
    REM Конфигурации нет в целевой папке, копируем из config/
    if exist "%CONFIG_DIR%\Configuration.json" (
        echo Копирование конфигурации по умолчанию из config\Configuration.json
        copy /Y "%CONFIG_DIR%\Configuration.json" "!MOD_TARGET!\" >nul
        if errorlevel 1 (
            echo [ОШИБКА] Не удалось скопировать Configuration.json
        ) else (
            echo [OK] Конфигурация скопирована
        )
    ) else (
        echo [ПРЕДУПРЕЖДЕНИЕ] Файл Configuration.json не найден в %CONFIG_DIR%
        echo [ПРЕДУПРЕЖДЕНИЕ] Мод будет использовать конфигурацию по умолчанию из кода
    )
)

echo [OK] Конфигурация проверена

REM ---------------------------------------------------------------------------
REM Проверка дополнительных файлов
REM ---------------------------------------------------------------------------
echo.
echo [6/9] Копирование дополнительных файлов...

REM Копирование DllNames.txt (ОБЯЗАТЕЛЬНО для загрузки мода Empyrion!)
if exist "%CONFIG_DIR%\GalacticExpansion_Info.yaml" (
    echo Копирование: GalacticExpansion_Info.yaml ^(обязательный файл мода^)
    copy /Y "%CONFIG_DIR%\GalacticExpansion_Info.yaml" "!MOD_TARGET!\" >nul
    if errorlevel 1 (
        echo [ОШИБКА] Не удалось скопировать GalacticExpansion_Info.yaml
    )
) else (
    echo [ПРЕДУПРЕЖДЕНИЕ] Файл GalacticExpansion_Info.yaml не найден - мод НЕ загрузится!
)

REM Копирование README, если существует
if exist "%PROJECT_DIR%\README.md" (
    echo Копирование: README.md
    copy /Y "%PROJECT_DIR%\README.md" "!MOD_TARGET!\" >nul
)

REM Копирование CHANGELOG, если существует
if exist "%PROJECT_DIR%\CHANGELOG.md" (
    echo Копирование: CHANGELOG.md
    copy /Y "%PROJECT_DIR%\CHANGELOG.md" "!MOD_TARGET!\" >nul
)

echo [OK] Дополнительные файлы обработаны

REM ---------------------------------------------------------------------------
REM Очистка старых логов мода в папке игры
REM view_logs.cmd копирует *.log из Content\Mods\GalacticExpansion\Logs
REM в папку проекта. Здесь те же файлы удаляются, чтобы следующий запуск
REM игры писал логи с чистого листа.
REM Архивы в Logs\archives\ не трогаем: view_logs.cmd их не копирует.
REM ---------------------------------------------------------------------------
echo.
echo [7/9] Очистка старых логов мода...

set "MOD_LOGS_DIR=!MOD_TARGET!\Logs"
set "DELETED_LOGS=0"
set "FAILED_LOGS=0"

if exist "!MOD_LOGS_DIR!\*.log" (
    echo Папка логов: !MOD_LOGS_DIR!
    for %%F in ("!MOD_LOGS_DIR!\*.log") do (
        del /F /Q "%%F" >nul 2>&1
        if not errorlevel 1 (
            echo Удалён лог: %%~nxF
            set /a DELETED_LOGS+=1
        ) else (
            echo [ПРЕДУПРЕЖДЕНИЕ] Не удалось удалить ^(файл занят^): %%~nxF
            set /a FAILED_LOGS+=1
        )
    )
    echo [OK] Удалено логов: !DELETED_LOGS!
    if !FAILED_LOGS! GTR 0 (
        echo [ПРЕДУПРЕЖДЕНИЕ] Не удалено логов: !FAILED_LOGS!
    )
) else (
    echo Старые логи не найдены ^(очистка не требуется^)
)

REM ---------------------------------------------------------------------------
REM Очистка старых бэкапов (оставляем только последние 10 архивов)
REM ---------------------------------------------------------------------------
echo.
echo [8/9] Очистка старых бэкапов...

REM Подсчет количества ZIP-архивов бэкапов
set "BACKUP_COUNT=0"
for %%F in ("!MOD_TARGET!\backups\backup_*.zip") do set /a BACKUP_COUNT+=1

REM Если архивов больше 10, удаляем самые старые
if !BACKUP_COUNT! GTR 10 (
    echo Найдено !BACKUP_COUNT! архивов, удаление старых...
    REM Оставляем последние 10 файлов (сортируем по дате и удаляем первые N-10)
    set /a "TO_DELETE=!BACKUP_COUNT!-10"
    
    REM Формируем список файлов для удаления
    set "DELETE_INDEX=0"
    for /f "delims=" %%F in ('dir /b /o:d "!MOD_TARGET!\backups\backup_*.zip"') do (
        set /a DELETE_INDEX+=1
        if !DELETE_INDEX! LEQ !TO_DELETE! (
            echo Удаление старого архива: %%F
            del "!MOD_TARGET!\backups\%%F" >nul 2>&1
        )
    )
) else (
    echo Архивов: !BACKUP_COUNT! ^(очистка не требуется^)
)

echo [OK] Очистка завершена

REM ---------------------------------------------------------------------------
REM Вывод итогов
REM ---------------------------------------------------------------------------
echo.
echo [9/9] Завершение...
echo.
echo =========================================================================
echo Обновление завершено успешно!
echo =========================================================================
echo.
echo Итоговая сводка:
echo   - Конфигурация: %BUILD_CONFIG%
echo   - Скопировано файлов: %COPIED_COUNT%
echo   - Удалено старых логов: !DELETED_LOGS!
if %FAILED_COUNT% GTR 0 (
    echo   - Ошибок при копировании: %FAILED_COUNT%
)
if !FAILED_LOGS! GTR 0 (
    echo   - Не удалось удалить логов: !FAILED_LOGS!
)
echo   - Целевая папка: !MOD_TARGET!
echo.
echo Мод готов к запуску!
echo =========================================================================

endlocal
exit /b 0
