# Tech Context — GalacticExpansion (GLEX)

## Стек

- C# 8.0+, .NET Framework 4.8
- Newtonsoft.Json 13.x
- NLog 5.x
- xUnit 2.6+, Moq 4.20+
- **UnityEngine.CoreModule** (из Empyrion) - для IPlayfield типов

## Внешние зависимости (Empyrion)

- `ModApi.dll`, `Mif.dll`, `UnityEngine.CoreModule.dll`
- Расположение: `lib/` (копируются из Empyrion DedicatedServer)

## Ключевые ModAPI возможности

- Спавн/удаление сущностей: `Request_Entity_Spawn`, `Request_Entity_Destroy`
- Список структур: `Request_GlobalStructure_List`
- Защита от decay: `Request_Structure_Touch`
- **IPC коммуникация**: `INetwork.SendToPlayfieldServer()`, `INetwork.SendToDedicatedServer()`. Вызов только из `EmpyrionModChannel` (`GalacticExpansion.dll`): ключ колбэка = `GetCallingAssembly().GetName().Name`
- **Определение процесса**: `IModApi.Application.Mode` (DedicatedServer / PlayfieldServer)
- **Готовность playfield**: `IModApi.Application.OnPlayfieldLoaded` (IPlayfield instance)
- **Прямой спавн (API2)**: `IPlayfield.SpawnPrefab()`, `IPlayfield.SpawnEntity()`, `IPlayfield.GetTerrainHeightAt()`

## Структура проекта

- `src/GalacticExpansion.Core` — модули
  - `IPC/` - IPC протокол, NetworkBridge, IEmpyrionModChannel, PlayfieldReadyNotification
  - `Spawning/` - EntitySpawner, IPCEntitySpawner, NativePlayfieldSpawner, StageManager
  - `Simulation/` - SimulationEngine, ColonyManager, ColonyTickModule
  - `Economy/` - EconomySimulator, UnitEconomyManager
  - `Gateway/` - EmpyrionGateway для ModAPI
- `src/GalacticExpansion.Models` — модели данных
- `src/GalacticExpansion` — entry point (ModMain), `IPC/EmpyrionModChannel.cs` (единственные вызовы ModApi.Network)
- `lib/` - Empyrion DLLs

## Локальная разработка

- Сборка: `dotnet build src/GalacticExpansion.sln --configuration Release`
- Тесты: `dotnet test src/GalacticExpansion.sln`
- **ВАЖНО:** При NuGet proxy проблемах: `--no-restore`

## Логирование

- NLog, путь к конфигу задается явно в `ModMain`
- **Префиксы процессов**: `[Dedi-PID]` / `[PfServer-PID]`
- Layout: `${longdate}|${level}|${logger}|${gdc:item=process}|${message}`

## State Management

### Принцип: In-memory state = единственный источник правды
- `SimulationEngine._state` — канонический объект SimulationState
- `_stateStore.LoadAsync()` — **ТОЛЬКО при запуске** (StartAsync)
- `_stateStore.SaveAsync()` — автосохранение каждые 60 сек + при shutdown
- **ЗАПРЕЩЕНО** LoadAsync внутри тикового цикла!

### Конфигурация стадий

Числа баланса (и в `Zirax.Stages` рабочего конфига, и в дефолте загрузчика):

```
ConstructionYard: RequiredResources=0, ProductionRate=100, MinTime=600s
BaseL1: RequiredResources=1000, ProductionRate=150, MinTime=1800s
BaseL2: RequiredResources=3000, ProductionRate=200, MinTime=3600s
BaseL3: RequiredResources=6000, ProductionRate=250, MinTime=7200s
BaseMax: RequiredResources=10000, ProductionRate=300, MinTime=14400s
```

Имена префабов в дефолте загрузчика (`BA_ConstructionSite`, `BA_Zirax_Small_1`, `BA_Zirax_Medium_1`, `BA_Zirax_Large_1`) в `Content/Prefabs` нет. Рабочий `config/Configuration.json`: стадии `BA_ZiraxOutpost`, аванпосты `BA_ZiraxSkyminer`. Игра читает копию в `Content/Mods/GalacticExpansion/Configuration.json`. `deploy_mod.cmd` этот файл не затирает.

## Multi-Process Architecture

### Dedi процесс:
```
IMod.Init → InitializeGatewayAndModulesForDedi
  → PlacementResolver (дистанции; финальную Y не считает)
  → IPCEntitySpawner(networkBridge, placementResolver, Dedi, logger) — БЕЗ EntitySpawner!
  → регистрация модулей → SimulationEngine.StartAsync() [немедленно]
  → _simulationStarted = true → SetSimulationState(_state)
NetworkBridge.OnPlayfieldReadyReceived → EnsurePlayfieldColoniesSpawnedAsync
Game_Update (guard _simulationStarted) → Task.Run(TryMaterialize) [throttle 3s]
  → IPCEntitySpawner.SpawnStructureAtTerrainAsync
    → SpawnViaIPCAsync (snap=true, X/Z, hoff) → NetworkBridge → PfServer
      → GetTerrainHeightAt + offset → SpawnPrefab
```

### PfServer процесс:
```
OnPlayfieldLoaded → IPlayfield → NativePlayfieldSpawner
  → NetworkBridge.SendNotificationToDedi(PlayfieldReadyNotification) [retry 3×500ms]
NetworkBridge → HandleIPCRequestWithNativeSpawner
```

### КРИТИЧНО: EntitySpawner на Dedi ЗАПРЕЩЁН
Спавн через ModAPI Gateway на Dedi не работает (Event_Ok без entity ID).
IPCEntitySpawner на Dedi НЕ имеет _directSpawner — только IPlacementResolver + NetworkBridge.

## Threading Model (КРИТИЧНО!)

**Empyrion = Unity → SynchronizationContext на main thread.**

- `Game_Update()`, `Game_Event()` — Unity main thread
- `SimulationEngine` тики — `Task.Run` (ThreadPool)
- **ЗАПРЕЩЕНО** `.GetAwaiter().GetResult()` / `.Wait()` / `.Result` в Game_Update → deadlock!
- **Правильно:** `_ = Task.Run(async () => await ...)` + throttle + guard

## IPC: кто вызывает INetwork

`EmpyrionModChannel` в сборке мода. `NetworkBridge` в Core принимает `IEmpyrionModChannel` и не трогает `IModApi.Network`.

`SendToPlayfieldServer` на dedicated возвращает `false`, пока PfServer процесса нет. `SendToDedicatedServer` в dedicated-сборке — заглушка `false`; рабочая реализация у клиентской сборки, которой пользуется PfServer. Наоборот тоже: с плейфилда `SendToPlayfieldServer` всегда `false`.

`DeserializeMessage` читает поле `type` (`JObject`), затем `DeserializeObject` конкретного класса. `DeserializeObject<IPCMessage>` нельзя: класс абстрактный. В логе v5 путь `pf` — первое поле наследника.

## IPC протокол (типы сообщений)
- `SpawnStructure` / `SpawnStructureResponse` — спавн структуры. Поля `snap` и `hoff`: при `snap=true` PfServer заменяет Y высотой рельефа плюс отступ
- `SpawnNPC` / `SpawnNPCResponse` — спавн NPC
- `PlayfieldReady` / `PlayfieldReadyResponse` — проверка готовности
- `PlayfieldReadyNotification` — fire-and-forget уведомление (PfServer → Dedi) [retry 3×500ms]

## Известные нюансы

- В тестах использовать `IPlayfieldWrapper` вместо `IPlayfield`
- **UnityEngine.ILogger vs NLog.ILogger:** Добавлять alias при using UnityEngine
- `SpawnPrefab` принимает имя без `.epb`. Нет файла — `EntityId=-1`
- Точный Y структуры — `IPlayfield.GetTerrainHeightAt` на PfServer (`snap=true`). Ошибка чтения рельефа отменяет спавн. Кэш `PlacementResolver` на Dedi по-прежнему без playfield и для финальной Y не используется
- **IPC таймауты**: 15s структуры, 10s NPC
