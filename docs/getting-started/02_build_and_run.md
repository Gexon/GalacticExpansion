# Сборка и запуск (GLEX)

**Назначение**: описать путь “собрал DLL → положил на сервер → проверил, что загрузилось”.  
**Аудитория**: разработчики.  
**Статус**: draft.  
**Связанные документы**: [`01_dev_prerequisites.md`](01_dev_prerequisites.md), [`03_debugging_and_logs.md`](03_debugging_and_logs.md).  

## Артефакт мода

Для Dedicated Server мод представляет собой **DLL** (и, опционально, дополнительные файлы конфигурации/ресурсов).

Ключевое правило установки (согласно общему описанию ModAPI/сообщества): в папке мода должна лежать **одна DLL** (плюс ваши дополнительные файлы), а не набор зависимостей вразнобой.

## Рекомендуемая структура на сервере

Папка модов (создать, если отсутствует):

- `Content/Mods/`

Далее — отдельная папка под GLEX:

- `Content/Mods/GLEX/`
  - `GalacticExpansion.dll` (имя уточним, когда появится проект)
  - `ModAppConfig.json` (указатель на основной конфиг)
  - `Config/` (опционально) — если вы хотите хранить читаемые человеком конфиги рядом

> Почему так: большинство модов из `docs/examples` опираются на понятный “один мод = одна папка” и отдельные файлы конфигурации.

## Конфиг (как будет устроено в GLEX)

Планируемый паттерн:

- `Content/Mods/GLEX/ModAppConfig.json` указывает на файл конфигурации в `Saves/Games/<SaveGame>/Mods/GLEX/...`

Это соответствует подходу, который видно в примерах:

- `EmpyrionTeleporter`: `ModAppConfig.json` → `EmpyrionTeleporter/Teleporters.json`
- `EmpyrionStructureCleanUp`: `ModAppConfig.json` → `EmpyrionStructureCleanUp/StructureCleanUpSettings.json`

Подробности будут в `docs/config/ConfigReference.md`.

## Перезапуск сервера

После копирования DLL в `Content/Mods/GLEX/` требуется **перезапуск Dedicated Server**, чтобы мод загрузился.

## Проверка загрузки

Проверка делается по серверным логам (см. [`03_debugging_and_logs.md`](03_debugging_and_logs.md)):

- должны появиться строки вида `Loaded mod ...` для GLEX,
- при ошибках часто встречается `Error on executing Game_Event ...` с исключением.

## Важно про совместимость и версию игры

В `docs/00_наброски проекта.md` зафиксировано целевое окружение: Empyrion **v1.15 Experimental**.  
Если игра/ModAPI обновятся, возможны несовместимости на уровне типов `CmdId`, структур данных и поведения events/requests — это нужно документировать в `docs/architecture/Roadmap.md` и ADR.
