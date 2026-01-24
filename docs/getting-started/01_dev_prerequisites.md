# Подготовка окружения разработки (GLEX)

**Назначение**: быстро подготовить окружение для разработки server-side DLL мода Empyrion.  
**Аудитория**: разработчики.  
**Статус**: draft.  
**Связанные документы**: [`02_build_and_run.md`](02_build_and_run.md), [`03_debugging_and_logs.md`](03_debugging_and_logs.md), [`../overview/02_glossary.md`](../overview/02_glossary.md).  

## Что нужно понимать заранее

- GLEX — **server-side DLL мод**: он загружается Dedicated Server’ом, работает через ModAPI (интерфейс `ModInterface`) и не требует client-side модов.
- ModAPI построена вокруг двух каналов:
  - **Events** приходят в `Game_Event(CmdId eventId, ushort seqNr, object data)` (сервер → мод).
  - **Requests** отправляются через `ModGameAPI.Game_Request(CmdId reqId, ushort seqNr, object data)` (мод → сервер), а ответы обычно приходят как событие с тем же `seqNr`.

## Инструменты

- **IDE**: Visual Studio / Rider (на ваш выбор).
- **Целевая платформа**: .NET Framework (точная версия выбирается исходя из ограничений Empyrion/ModApi DLL; примеры из `docs/examples` часто используют `net472`).

## Где брать зависимости (DLL)

Типично для Empyrion server-side модов в проект добавляют ссылки на DLL из папок Dedicated Server’а (или из `dependencies/` в репозитории мода, как в примерах):

- `Mif.dll`
- `ModApi.dll`
- `protobuf-net.dll` (часто используется в экосистеме модов; см. `docs/examples`)

> Примечание: в этом репозитории пока нет “официальной” папки `dependencies/` для GLEX. Когда появится код — закрепим **единый способ** хранения/обновления этих DLL и опишем его здесь.

## Минимальная структура проекта (как в примерах)

В примерах из `docs/examples` часто встречается схема:

- один проект Class Library (`.csproj`), который содержит реализацию `ModInterface` (или обёртку вроде `SimpleMod`),
- рядом конфиги (JSON/YAML/XML), создаваемые/читаемые из `Saves/Games/<SaveGame>/Mods/<ModName>/...`,
- иногда используется ILMerge/упаковка, чтобы получить **одну** итоговую DLL.

См. также:
- `docs/examples/sample-empyrion-mod-master/DeathMessenger/DeathMessages.cs` — “голый” `ModInterface` и работа с `Game_Event`/`Game_Request`.
- `docs/examples/EmpyrionAPITools-master/README.md` — описание паттерна “requests/events + seqNr” и упрощений.

## Что за конфиги “ModAppConfig.json” в примерах

Во многих модах рядом с DLL лежит маленький файл `ModAppConfig.json`, который указывает, где искать основной конфиг. Например (референс из примеров):

- `docs/examples/EmpyrionTeleporter-master/EmpyrionTeleporter/ModAppConfig.json`
- `docs/examples/EmpyrionStructureCleanUp-master/EmpyrionStructureCleanUp/ModAppConfig.json`

GLEX будет использовать аналогичный подход (см. `docs/config/ConfigReference.md`).
