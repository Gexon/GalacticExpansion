@echo off
REM =============================================================================
REM Скрипт обновления мода GalacticExpansion после успешной сборки
REM =============================================================================
REM Автоматически создает бэкапы, копирует новые файлы в папку мода Empyrion
REM и сохраняет пользовательские настройки (Configuration.json и state.json)
REM
REM Использование:
REM   deploy_mod.cmd [build_config]
REM   build_config - конфигурация сборки (Debug или Release), по умолчанию Release
REM
REM Пути:
REM   Проект: E:\for_game\Empyrion\GalacticExpansion
REM   Папка модов Empyrion: D:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival\Content\Mods
REM =============================================================================

setlocal enabledelayedexpansion

REM ---------------------------------------------------------------------------
REM Конфигурация путей
REM ---------------------------------------------------------------------------
set "PROJECT_DIR=E:\for_game\Empyrion\GalacticExpansion"
set "EMPYRION_MODS=D:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival\Content\Mods"
set "MOD_NAME=GalacticExpansion"
set "MOD_TARGET=%EMPYRION_MODS%\%MOD_NAME%"

REM Конфигурация сборки (Debug или Release)
set "BUILD_CONFIG=%~1"
if "%BUILD_CONFIG%"=="" set "BUILD_CONFIG=Release"

REM Папка сборки (предполагаем стандартную структуру .NET проекта)
set "BUILD_DIR=%PROJECT_DIR%\bin\%BUILD_CONFIG%"

REM Временная метка для бэкапов
set "TIMESTAMP=%date:~-4%%date:~3,2%%date:~0,2%_%time:~0,2%%time:~3,2%%time:~6,2%"
set "TIMESTAMP=%TIMESTAMP: =0%"

REM ---------------------------------------------------------------------------
REM Проверка предварительных условий
REM ---------------------------------------------------------------------------
echo.
echo =========================================================================
echo Обновление мода GalacticExpansion
echo =========================================================================
echo.

echo [1/8] Проверка путей...

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
if not exist "%BUILD_DIR%\%MOD_NAME%.dll" (
    echo [ОШИБКА] Файл %MOD_NAME%.dll не найден в %BUILD_DIR%
    echo Выполните сборку проекта перед запуском этого скрипта
    exit /b 1
)

REM Проверка существования папки модов Empyrion
if not exist "%EMPYRION_MODS%" (
    echo [ОШИБКА] Папка модов Empyrion не найдена: %EMPYRION_MODS%
    exit /b 1
)

echo [OK] Все пути найдены

REM ---------------------------------------------------------------------------
REM Создание целевой папки мода, если не существует
REM ---------------------------------------------------------------------------
echo.
echo [2/8] Подготовка целевой папки...

if not exist "%MOD_TARGET%" (
    echo Создание новой папки мода: %MOD_TARGET%
    mkdir "%MOD_TARGET%"
    if errorlevel 1 (
        echo [ОШИБКА] Не удалось создать папку мода
        exit /b 1
    )
) else (
    echo Папка мода уже существует: %MOD_TARGET%
)

REM ---------------------------------------------------------------------------
REM Создание бэкапов существующих файлов (следуя Operations Runbook 5.1)
REM ---------------------------------------------------------------------------
echo.
echo [3/8] Создание бэкапов...

REM Создание папки для бэкапов, если не существует
if not exist "%MOD_TARGET%\backups" mkdir "%MOD_TARGET%\backups"

REM Бэкап основной DLL, если существует
if exist "%MOD_TARGET%\%MOD_NAME%.dll" (
    echo Создание бэкапа: %MOD_NAME%.dll ^-^> %MOD_NAME%.dll.%TIMESTAMP%.backup
    copy /Y "%MOD_TARGET%\%MOD_NAME%.dll" "%MOD_TARGET%\backups\%MOD_NAME%.dll.%TIMESTAMP%.backup" >nul
    if errorlevel 1 (
        echo [ПРЕДУПРЕЖДЕНИЕ] Не удалось создать бэкап DLL
    )
)

REM Бэкап state.json, если существует
if exist "%MOD_TARGET%\state.json" (
    echo Создание бэкапа: state.json ^-^> state_pre_update_%TIMESTAMP%.json
    copy /Y "%MOD_TARGET%\state.json" "%MOD_TARGET%\backups\state_pre_update_%TIMESTAMP%.json" >nul
    if errorlevel 1 (
        echo [ПРЕДУПРЕЖДЕНИЕ] Не удалось создать бэкап state.json
    )
)

echo [OK] Бэкапы созданы

REM ---------------------------------------------------------------------------
REM Копирование файлов мода (НЕ заменяя конфигурацию и state.json)
REM ---------------------------------------------------------------------------
echo.
echo [4/8] Копирование файлов мода...

REM Копирование основной DLL
echo Копирование: %MOD_NAME%.dll
copy /Y "%BUILD_DIR%\%MOD_NAME%.dll" "%MOD_TARGET%\" >nul
if errorlevel 1 (
    echo [ОШИБКА] Не удалось скопировать %MOD_NAME%.dll
    exit /b 1
)

REM Копирование PDB (отладочная информация) для Debug-сборки
if "%BUILD_CONFIG%"=="Debug" (
    if exist "%BUILD_DIR%\%MOD_NAME%.pdb" (
        echo Копирование: %MOD_NAME%.pdb (отладочные символы)
        copy /Y "%BUILD_DIR%\%MOD_NAME%.pdb" "%MOD_TARGET%\" >nul
    )
)

REM Копирование зависимостей (если есть)
REM TODO: Добавить список необходимых зависимостей по мере развития проекта
REM Пример:
REM if exist "%BUILD_DIR%\Newtonsoft.Json.dll" (
REM     echo Копирование зависимости: Newtonsoft.Json.dll
REM     copy /Y "%BUILD_DIR%\Newtonsoft.Json.dll" "%MOD_TARGET%\" >nul
REM )

echo [OK] Файлы мода скопированы

REM ---------------------------------------------------------------------------
REM Копирование конфигурации по умолчанию (только если не существует)
REM ---------------------------------------------------------------------------
echo.
echo [5/8] Проверка конфигурации...

REM Если есть файл Configuration.json в проекте (шаблон), копируем его только если не существует в целевой папке
if exist "%PROJECT_DIR%\Configuration.json" (
    if not exist "%MOD_TARGET%\Configuration.json" (
        echo Копирование конфигурации по умолчанию: Configuration.json
        copy /Y "%PROJECT_DIR%\Configuration.json" "%MOD_TARGET%\" >nul
        if errorlevel 1 (
            echo [ПРЕДУПРЕЖДЕНИЕ] Не удалось скопировать Configuration.json
        )
    ) else (
        echo Сохранение существующей конфигурации (НЕ перезаписываем Configuration.json)
    )
) else (
    echo [ПРЕДУПРЕЖДЕНИЕ] Файл Configuration.json не найден в проекте
)

echo [OK] Конфигурация проверена

REM ---------------------------------------------------------------------------
REM Проверка дополнительных файлов
REM ---------------------------------------------------------------------------
echo.
echo [6/8] Копирование дополнительных файлов...

REM Копирование README, если существует
if exist "%PROJECT_DIR%\README.md" (
    echo Копирование: README.md
    copy /Y "%PROJECT_DIR%\README.md" "%MOD_TARGET%\" >nul
)

REM Копирование CHANGELOG, если существует
if exist "%PROJECT_DIR%\CHANGELOG.md" (
    echo Копирование: CHANGELOG.md
    copy /Y "%PROJECT_DIR%\CHANGELOG.md" "%MOD_TARGET%\" >nul
)

echo [OK] Дополнительные файлы обработаны

REM ---------------------------------------------------------------------------
REM Очистка старых бэкапов (оставляем только последние 10)
REM ---------------------------------------------------------------------------
echo.
echo [7/8] Очистка старых бэкапов...

REM Подсчет количества бэкапов DLL
set "BACKUP_COUNT=0"
for %%F in ("%MOD_TARGET%\backups\%MOD_NAME%.dll.*.backup") do set /a BACKUP_COUNT+=1

REM Если бэкапов больше 10, удаляем самые старые
if !BACKUP_COUNT! GTR 10 (
    echo Найдено !BACKUP_COUNT! бэкапов, удаление старых...
    REM Оставляем последние 10 файлов (сортируем по дате и удаляем первые N-10)
    set /a "TO_DELETE=!BACKUP_COUNT!-10"
    
    REM Формируем список файлов для удаления
    set "DELETE_INDEX=0"
    for /f "delims=" %%F in ('dir /b /o:d "%MOD_TARGET%\backups\%MOD_NAME%.dll.*.backup"') do (
        set /a DELETE_INDEX+=1
        if !DELETE_INDEX! LEQ !TO_DELETE! (
            echo Удаление старого бэкапа: %%F
            del "%MOD_TARGET%\backups\%%F" >nul 2>&1
        )
    )
) else (
    echo Бэкапов: !BACKUP_COUNT! (очистка не требуется)
)

echo [OK] Очистка завершена

REM ---------------------------------------------------------------------------
REM Вывод итогов
REM ---------------------------------------------------------------------------
echo.
echo [8/8] Завершение...
echo.
echo =========================================================================
echo Обновление завершено успешно!
echo =========================================================================
echo.
echo Конфигурация сборки: %BUILD_CONFIG%
echo Целевая папка: %MOD_TARGET%
echo Время: %date% %time%
echo.
echo ВАЖНО: Следующие шаги (согласно Operations Runbook 5.1):
echo   1. Остановите Empyrion Dedicated Server (если запущен)
echo   2. Проверьте CHANGELOG новой версии на предмет миграций
echo   3. Запустите сервер
echo   4. Проверьте логи на наличие ошибок:
echo      - [INFO] GalacticExpansion vX.X loading...
echo      - Проверьте на наличие ошибок инициализации
echo.
echo Бэкапы сохранены в: %MOD_TARGET%\backups\
echo В случае проблем, используйте откат (Operations Runbook 5.2)
echo.
echo =========================================================================

endlocal
exit /b 0
