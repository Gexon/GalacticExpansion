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

## Ключевые технические решения

### In-Memory State Pattern (Phase 3.2 — КРИТИЧЕСКОЕ!)

**Принцип:** `SimulationEngine._state` — единственный источник правды во время работы симуляции. Файл `state.json` — только для персистентности между перезапусками.

**Правила:**
1. **ЗАПРЕЩЕНО** вызывать `_stateStore.LoadAsync()` внутри тикового цикла
2. Модули работают напрямую с объектами из `_state.Colonies`
3. `_state.IsDirty = true` ставится каждый тик
4. Автосохранение в файл каждые 60 сек + при shutdown
5. `ColonyManager.SetSimulationState()` инжектит ссылку на in-memory state после старта SimulationEngine

**Поток данных:**
```
Запуск: LoadAsync() -> _state (in-memory)
Тик: context.CurrentState = _state -> модули работают напрямую
Game_Update: TryMaterializePendingColoniesAsync() -> in-memory state
Автосохранение (60 сек): SaveAsync(_state) -> state.json
Shutdown: SaveAsync(_state) -> state.json
```

**Ошибка-предшественник:** SimulationEngine перезагружал state из файла каждый тик (`_stateStore.LoadAsync()`), уничтожая все in-memory изменения VirtualResources.

### Logistics Ship Resources Pattern (Phase 3.2)

**Принцип:** Логистический корабль доставляет начальные ресурсы при создании колонии. Ресурсов достаточно для ConstructionYard + BaseL1. Самостоятельное накопление через ProductionRate — только с BaseL2.

**Формула:** `initialResources = (ConstructionYard.RequiredResources + BaseL1.RequiredResources) * 1.1`

С текущим конфигом: `(0 + 1000) * 1.1 = 1100 VirtualResources`

### Native Playfield Spawner Pattern (Phase 3.1)

**Проблема:** PfServer крашится при инициализации из-за отсутствия ModGameAPI.
**Решение:** Прямой спавн через IPlayfield API без Gateway/ModGameAPI.

```
OnPlayfieldLoaded → IPlayfield instance
    → NativePlayfieldSpawner(pfInstance)
    → IPlayfield.SpawnPrefab() / SpawnEntity() (СИНХРОННЫЙ!)
```

### Interface Contract Alignment
- Реализации обязаны повторять порядок параметров и смысл контрактов интерфейсов.
- Тесты проверяют соответствие контрактам.

### Event_Error Handling (ModAPI)
- При ответе игры на запрос с ошибкой приходит `CmdId.Event_Error` и объект `ErrorInfo`.
- Шлюз обрабатывает Event_Error: вызывает `SequenceManager.CompleteWithError(seqNr, exception)`.

### Playfield Load Handling & Colony Virtualization (Phase 3)

1. **Создание виртуальной колонии** (при старте мода):
   - `ColonyTickModule.InitializeAsync` → `CreateColonyAsync` → `InitializeColonyAsync`
   - `IsVirtual = true`, начальные ресурсы = 1100
   - Развивается в памяти: ресурсы, юниты, переходы стадий

2. **Материализация** (через IPC из Dedi в PfServer):
   - `Event_Playfield_Loaded` → `EnsurePlayfieldColoniesSpawnedAsync` → `PendingMaterialization = true`
   - `Game_Update` → `TryMaterializePendingColoniesAsync()` → проверка готовности → IPC
   - PfServer: `HandleIPCRequestWithNativeSpawner()` → `NativePlayfieldSpawner`

## Важные инварианты

1. **In-memory state — единственный источник правды** во время работы симуляции.
2. **Файл state.json** — только для персистентности (автосохранение 60 сек + shutdown).
3. **ЗАПРЕЩЕНО** `_stateStore.LoadAsync()` внутри тикового цикла.
4. `SeqNr` уникальны и корректно сопоставляются.
5. Rate limiting обязателен для ModAPI запросов (только Dedi).
6. Спавн в Dedi через `IEntitySpawner` (IPCEntitySpawner).
7. Спавн в PfServer через `NativePlayfieldSpawner` (прямой IPlayfield API).
8. **Виртуальные колонии:** физические операции запрещены; только обновление в памяти.
9. **Multi-process:** Spawn работает только из PfServer процесса.
10. **UnityEngine.ILogger:** При using UnityEngine добавлять `using ILogger = NLog.ILogger;`
