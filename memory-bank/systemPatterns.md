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

**Принцип:** При материализации спавнится префаб текущей стадии колонии из `_config.Zirax.Stages`, где `Stage` равен `colony.Stage.ToString()` (`ConstructionYard`, `BaseL1`, `BaseL2`, `BaseL3`, `BaseMax`).

`PrefabName` — имя файла в `Content/Prefabs` без `.epb`. Нет файла — `SpawnPrefab` возвращает `-1`. Пустой `Zirax.Stages` загрузчик заменяет дефолтами из `ConfigurationLoader`; эти имена в ванильной игре отсутствуют.

Секции вне `Configuration` (`Prefabs`, `ColonyEvolution`, `Economy`, `ThreatDirector`, `Hostility`, `Advanced`) не читаются: `MemberSerialization.OptIn`.

### Высота рельефа только в PfServer (02.10.2026)

`IPlayfield.GetTerrainHeightAt` есть там, где сработал `OnPlayfieldLoaded`. Dedi не подставляет Y: `SpawnStructureAtTerrainAsync` шлёт `SnapToTerrain` и отступ. PfServer считает `Y = GetTerrainHeightAt + HeightOffset` (для базы 0.5 м) и только потом вызывает `SpawnPrefab`. Ошибка чтения рельефа отменяет спавн. Абсолютный `SpawnStructureAsync` Y не трогает. Проверено в игре: структура на земле.

### Interface Contract Alignment
- Реализации обязаны повторять порядок параметров и смысл контрактов интерфейсов.

### ЗАПРЕТ на EntitySpawner в Dedi процессе (Phase 3.5)

**Принцип:** На Dedi `EntitySpawner` НЕ создаётся и НЕ используется. Спавн через ModAPI Gateway на Dedi не работает — Empyrion отвечает `Event_Ok` без entity ID.

**Архитектура IPCEntitySpawner:**
- **Dedi-конструктор:** `(NetworkBridge, IPlacementResolver, IEmpyrionGateway, ApplicationMode, ILogger)` — без IEntitySpawner. Gateway только для `Request_Entity_Destroy`, не для спавна.
- **PfServer-конструктор:** `(IEntitySpawner, ApplicationMode, ILogger)` — без NetworkBridge
- На Dedi `_directSpawner = null`. `DestroyEntityAsync` возвращает `bool`: сначала `Request_Entity_Destroy` (снос базы), затем IPC `RemoveEntity`, затем `EntityExists`. Исключение не бросает. Пока id ещё на playfield, `StageManager` не ставит новый префаб и не меняет стадию.
- `EntityExistsAsync` при обрыве IPC возвращает `false`.
- Высоту рельефа на Dedi не считать. Финальный Y — только `GetTerrainHeightAt` на PfServer при `SnapToTerrain`

### Канал INetwork = имя вызывающей сборки (01.10.2026)

**Принцип:** `ModApi.Network.RegisterReceiver*` и `Send*` вызываются только из `EmpyrionModChannel` в сборке `GalacticExpansion.dll`.

Игра ключует колбэк через `GetCallingAssembly().GetName().Name`. Вызов из Core даёт ключ `GalacticExpansion.Core`. Строка `receiver` `"GalacticExpansion"` этот ключ не находит. `Send*` может вернуть `true`, колбэк не вызывается, лога игры нет.

`sender` в колбэке — сборка, которая вызвала `Send*`. Методы канала помечены `NoInlining`: инлайн в Core снова подменит сборку.

Направления: Dedi — `RegisterReceiverForPlayfieldPackets` + `SendToPlayfieldServer`. PfServer — `RegisterReceiverForDediPackets` + `SendToDedicatedServer`. Обратный `Send*` в том же процессе — заглушка и возвращает `false`.

`DeserializeMessage` не создаёт абстрактный `IPCMessage`. Сначала поле `type`, потом конкретный класс.

Подробности: `docs/architecture/12_Multi_Process_IPC_Architecture.md`, раздел «Канал INetwork — имя вызывающей сборки».

### TryCompleteResponse: null-safe (Phase 3.5)

**Принцип:** `EmpyrionGateway.TryCompleteResponse` проверяет `data == null` перед `data.GetType()`. Event_Ok от Empyrion приходит с null data — рефлексия невозможна, возвращаем false.

## Важные инварианты

1. **In-memory state — единственный источник правды** во время работы симуляции.
2. **ЗАПРЕЩЕНО** `_stateStore.LoadAsync()` внутри тикового цикла.
3. **Симуляция стартует немедленно** — не ждёт playfield / игроков.
4. **Материализация по IPC** — PfServer → PlayfieldReadyNotification → Dedi.
5. **ЗАПРЕЩЕНО** `.GetAwaiter().GetResult()` в Game_Update — deadlock!
6. **Throttle + guard** обязательны для fire-and-forget из Game_Update.
7. `SeqNr` уникальны и корректно сопоставляются.
8. Rate limiting обязателен для ModAPI запросов (только Dedi).
9. **ЗАПРЕЩЕНО** `EntitySpawner` на Dedi! Только `IPCEntitySpawner(NetworkBridge, IPlacementResolver)`.
10. В PfServer спавн через `NativePlayfieldSpawner` или `_directSpawner`.
11. **Виртуальные колонии:** физические операции запрещены; только обновление в памяти.
12. **Multi-process:** Spawn работает ТОЛЬКО из PfServer процесса.
12a. **INetwork:** вызывать только из `EmpyrionModChannel` (`GalacticExpansion.dll`). Не из Core.
13. **UnityEngine.ILogger:** При using UnityEngine добавлять `using ILogger = NLog.ILogger;`
14. **TryCompleteResponse:** `data == null` → return false (Event_Ok).
15. **Префаб стадии** только из `Zirax.Stages`. Имя должно существовать в `Content/Prefabs`.
16. **Высота структуры** — `GetTerrainHeightAt` на PfServer при `SnapToTerrain`. Запасные 100 м с Dedi в `SpawnPrefab` не передавать.
