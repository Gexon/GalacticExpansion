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

### Playfield Load Handling (Event_Playfield_Loaded)
- Событие `CmdId.Event_Playfield_Loaded` приходит с данными типа `PlayfieldLoad` (Mif/Eleon.Modding) с полями `sec`, `playfield`, `processId`.
- `ColonyTickModule` подписан на `IEmpyrionGateway.GameEventReceived` и при `Event_Playfield_Loaded`:
  - Извлекает имя playfield из объекта `PlayfieldLoad`.
  - Если это `HomePlayfield` и state пустой (и экспансия включена) — создаёт первую колонию только после загрузки этого playfield (никакого спавна в незагруженный playfield).
  - Для любого playfield вызывает `IColonyManager.EnsurePlayfieldColoniesSpawnedAsync(playfield)` — обновление/защита уже существующих структур колоний на этом playfield, база для дальнейшего спавна виртуальных структур/юнитов при загрузке playfield или входе игрока.

## Важные инварианты

1. `State.json` всегда валиден (атомарная запись + бэкапы).
2. `SeqNr` уникальны и корректно сопоставляются.
3. Rate limiting обязателен для ModAPI запросов.
4. Спавн структур и NPC идет через `IEntitySpawner`.
5. Переходы стадий всегда сопровождаются синхронизацией state.
