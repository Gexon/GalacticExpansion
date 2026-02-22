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

### In-Memory State Pattern (Phase 3.2)

**Принцип:** `SimulationEngine._state` — единственный источник правды во время работы симуляции.

**Правила:**
1. **ЗАПРЕЩЕНО** вызывать `_stateStore.LoadAsync()` внутри тикового цикла
2. Модули работают напрямую с объектами из `_state.Colonies`
3. Автосохранение в файл каждые 60 сек + при shutdown
4. `ColonyManager.SetSimulationState()` инжектит ссылку на in-memory state

### Immediate Simulation Start (Phase 3.3)

**Принцип:** Симуляция запускается сразу в конце `InitializeGatewayAndModulesForDedi`, не дожидаясь загрузки playfield или подключения игроков. Колонии живут автономно.

**Обоснование:** Колонии должны развиваться независимо от игроков. После перезапуска сервера виртуальная симуляция (ресурсы, стадии) работает с первой секунды.

### IPC PlayfieldReadyNotification (Phase 3.3)

**Принцип:** Материализация колоний запускается по сигналу от PfServer, а не по `Event_Playfield_Loaded` на Dedi.

**Цепочка:**
```
PfServer: OnPlayfieldLoaded → NativePlayfieldSpawner создан → IPC готов
  → SendNotificationToDedi(PlayfieldReadyNotification{Playfield})
Dedi: OnPlayfieldReadyReceived → EnsurePlayfieldColoniesSpawnedAsync(playfield)
  → PendingMaterialization = true
Game_Update: TryMaterializePendingColoniesAsync → IPC спавн через PfServer
```

**Гарантии:**
- Сигнал идёт именно с того PfServer, который обслуживает playfield колонии
- PfServer уже полностью загружен (IPlayfield готов, NativePlayfieldSpawner создан)
- `_simulationStarted` guard: материализация вызывается только после старта симуляции

### Logistics Ship Resources Pattern (Phase 3.2)

**Формула:** `initialResources = (ConstructionYard.RequiredResources + BaseL1.RequiredResources) * 1.1 = 1100`

### Native Playfield Spawner Pattern (Phase 3.1)

```
OnPlayfieldLoaded → IPlayfield instance → NativePlayfieldSpawner(pfInstance)
  → IPlayfield.SpawnPrefab() / SpawnEntity() (СИНХРОННЫЙ!)
```

### MaterializeColonyAsync: Stage-Based Prefab (Phase 3.3)

**Принцип:** При материализации спавнится префаб текущей стадии колонии из `_config.Zirax.Stages`, а не хардкод из DropShips.

### Interface Contract Alignment
- Реализации обязаны повторять порядок параметров и смысл контрактов интерфейсов.

## Важные инварианты

1. **In-memory state — единственный источник правды** во время работы симуляции.
2. **ЗАПРЕЩЕНО** `_stateStore.LoadAsync()` внутри тикового цикла.
3. **Симуляция стартует немедленно** — не ждёт playfield / игроков.
4. **Материализация по IPC** — PfServer → PlayfieldReadyNotification → Dedi.
5. **Guard `_simulationStarted`** — TryMaterializePendingColoniesAsync только после старта.
6. `SeqNr` уникальны и корректно сопоставляются.
7. Rate limiting обязателен для ModAPI запросов (только Dedi).
8. Спавн в Dedi через `IPCEntitySpawner`, в PfServer через `NativePlayfieldSpawner`.
9. **Виртуальные колонии:** физические операции запрещены; только обновление в памяти.
10. **Multi-process:** Spawn работает только из PfServer процесса.
11. **UnityEngine.ILogger:** При using UnityEngine добавлять `using ILogger = NLog.ILogger;`
