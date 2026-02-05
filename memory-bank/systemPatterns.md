# System Patterns — GalacticExpansion (GLEX)

## Архитектурные паттерны

### Модульный монолит
- Единая DLL с четкими границами модулей.
- Расширяемость через интерфейсы и события.

### Event-Driven Architecture
- Взаимодействие через `EventBus`.
- Минимизация прямых зависимостей между модулями.

### Repository Pattern
- `IStateStore` управляет загрузкой/сохранением state.json.
- Атомарная запись и бэкапы — обязательны.

### Command Pattern
- AIM-команды оборачиваются в команды и валидируются.

## Ключевые технические решения

### Wrapper Pattern (изоляция Unity)
- `IPlayfieldWrapper` изолирует Unity-зависимости `IPlayfield`.
- Моки `IPlayfieldWrapper` работают в unit-тестах.

### State Sync Pattern
- Изменения колонии в критичных методах делаются через объект из `StateStore.LoadAsync`.
- После обновления — обязательное `SaveAsync`.

### Interface Contract Alignment
- Реализации обязаны повторять порядок параметров и смысл контрактов интерфейсов.
- Тесты проверяют соответствие контрактам.

### Event_Error Handling (ModAPI)
- При ответе игры на запрос с ошибкой приходит `CmdId.Event_Error` и объект `ErrorInfo` (поле `errorType` — enum `ErrorType`).
- Шлюз в `HandleEvent` обрабатывает Event_Error до попытки завершить ответ ожидаемым типом: извлекает текст из `ErrorInfo` (в т.ч. `errorType.ToString()` для понятного кода в логах), вызывает `SequenceManager.CompleteWithError(seqNr, exception)` — вызывающий получает исключение вместо "Type mismatch".

### Playfield Load Handling & Colony Virtualization (Phase 3 — ОБНОВЛЕНО)

**Виртуализация колоний** — ключевой паттерн для надёжного создания и развития колоний:

1. **Создание виртуальной колонии** (при старте мода):
   - Не зависит от событий — создаётся сразу в `ColonyTickModule.InitializeAsync`
   - `IsVirtual = true`, без физических структур
   - Развивается в БД: ресурсы, юниты, переходы стадий

2. **Материализация** (Event_Playfield_Loaded + retry):
   - Событие `CmdId.Event_Playfield_Loaded` → пометка `PendingMaterialization = true`
   - Каждый тик: `TryMaterializePendingColoniesAsync` (до 10 попыток)
   - При успехе: спавн структур, `IsVirtual = false`
   - Решает проблему `PlayfieldConnectionNotFound` (playfield еще не готов)

3. **EnsurePlayfieldColoniesSpawnedAsync:**
   - Помечает виртуальные колонии для материализации
   - Защищает (Touch) структуры материализованных колоний

**Данные события:** `PlayfieldLoad` (Mif/Eleon.Modding) — поля `sec`, `playfield`, `processId`

## Важные инварианты

1. `State.json` всегда валиден (атомарная запись + бэкапы).
2. `SeqNr` уникальны и корректно сопоставляются.
3. Rate limiting обязателен для ModAPI запросов.
4. Спавн структур и NPC идет через `IEntitySpawner`.
5. Переходы стадий всегда сопровождаются синхронизацией state.
6. **Виртуальные колонии:** физические операции (спавн, Touch, destroy) запрещены; только обновление моделей в БД.
7. **Материализация:** обязательно через retry-логику (до 10 попыток) для решения `PlayfieldConnectionNotFound`.
