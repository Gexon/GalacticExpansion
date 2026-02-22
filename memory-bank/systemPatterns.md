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

**Принцип:** Симуляция запускается сразу в конце `InitializeGatewayAndModulesForDedi`, не дожидаясь загрузки playfield или подключения игроков.

### ЗАПРЕТ на .GetAwaiter().GetResult() в Game_Update (Phase 3.4)

**Принцип:** НИКОГДА не вызывать `.GetAwaiter().GetResult()` (или `.Wait()`, `.Result`) на async-методах из `Game_Update()` или любого callback Empyrion.

**Причина:** Empyrion работает на Unity. `await` внутри async-методов захватывает `SynchronizationContext` (Unity main thread). `.GetResult()` блокирует main thread → continuation не может вернуться → **deadlock навсегда**.

**Правильный паттерн:**
```csharp
// НЕЛЬЗЯ (deadlock!):
_colonyManager.TryMaterializePendingColoniesAsync().GetAwaiter().GetResult();

// ПРАВИЛЬНО (fire-and-forget на ThreadPool):
_ = Task.Run(async () => {
    await _colonyManager.TryMaterializePendingColoniesAsync();
});
```

**Throttle + guard:** Для fire-and-forget из Game_Update обязательны:
- `_lastAttempt` timestamp — throttle (не чаще раза в N секунд)
- `volatile bool _inProgress` — защита от параллельных вызовов

### IPC PlayfieldReadyNotification (Phase 3.3 + 3.4)

**Принцип:** Материализация колоний запускается по сигналу от PfServer, а не по опросу `Request_Playfield_Stats`.

**Цепочка:**
```
PfServer: OnPlayfieldLoaded → NativePlayfieldSpawner создан → IPC готов
  → SendNotificationToDedi(PlayfieldReadyNotification) [retry 3×500ms]
Dedi: OnPlayfieldReadyReceived → EnsurePlayfieldColoniesSpawnedAsync(playfield)
  → PendingMaterialization = true
Fallback: Event_Playfield_Loaded → ColonyTickModule → EnsurePlayfield (идемпотентно)
Game_Update: Task.Run(TryMaterialize) [throttle 3s] → IPC спавн через PfServer
```

**Гарантии:**
- PfServer полностью загружен (NativePlayfieldSpawner создан, IPC handler зарегистрирован)
- Retry обеспечивает надёжность доставки уведомления
- Fallback через Event_Playfield_Loaded — второй путь на случай потери IPC

### Logistics Ship Resources Pattern (Phase 3.2)

**Формула:** `initialResources = (ConstructionYard.RequiredResources + BaseL1.RequiredResources) * 1.1 = 1100`

### Native Playfield Spawner Pattern (Phase 3.1)

```
OnPlayfieldLoaded → IPlayfield instance → NativePlayfieldSpawner(pfInstance)
  → IPlayfield.SpawnPrefab() / SpawnEntity() (СИНХРОННЫЙ!)
```

### MaterializeColonyAsync: Stage-Based Prefab (Phase 3.3)

**Принцип:** При материализации спавнится префаб текущей стадии колонии из `_config.Zirax.Stages`.

### Interface Contract Alignment
- Реализации обязаны повторять порядок параметров и смысл контрактов интерфейсов.

## Важные инварианты

1. **In-memory state — единственный источник правды** во время работы симуляции.
2. **ЗАПРЕЩЕНО** `_stateStore.LoadAsync()` внутри тикового цикла.
3. **Симуляция стартует немедленно** — не ждёт playfield / игроков.
4. **Материализация по IPC** — PfServer → PlayfieldReadyNotification → Dedi.
5. **ЗАПРЕЩЕНО** `.GetAwaiter().GetResult()` в Game_Update — deadlock!
6. **Throttle + guard** обязательны для fire-and-forget из Game_Update.
7. `SeqNr` уникальны и корректно сопоставляются.
8. Rate limiting обязателен для ModAPI запросов (только Dedi).
9. Спавн в Dedi через `IPCEntitySpawner`, в PfServer через `NativePlayfieldSpawner`.
10. **Виртуальные колонии:** физические операции запрещены; только обновление в памяти.
11. **Multi-process:** Spawn работает только из PfServer процесса.
12. **UnityEngine.ILogger:** При using UnityEngine добавлять `using ILogger = NLog.ILogger;`
