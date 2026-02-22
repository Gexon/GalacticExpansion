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
- **IPC коммуникация**: `INetwork.SendToPlayfieldServer()`, `INetwork.SendToDedicatedServer()`
- **Определение процесса**: `IModApi.Application.Mode` (DedicatedServer / PlayfieldServer)
- **Готовность playfield**: `IModApi.Application.OnPlayfieldLoaded` (IPlayfield instance)
- **Прямой спавн (API2)**: `IPlayfield.SpawnPrefab()`, `IPlayfield.SpawnEntity()`, `IPlayfield.GetTerrainHeightAt()`

## Структура проекта

- `src/GalacticExpansion.Core` — модули
  - `IPC/` - IPC протокол и NetworkBridge
  - `Spawning/` - EntitySpawner, IPCEntitySpawner, NativePlayfieldSpawner, StageManager
  - `Simulation/` - SimulationEngine, ColonyManager, модули
  - `Economy/` - EconomySimulator, UnitEconomyManager
  - `Gateway/` - EmpyrionGateway для ModAPI
- `src/GalacticExpansion.Models` — модели данных
- `src/GalacticExpansion` — entry point (ModMain)
- `lib/` - Empyrion DLLs

## Локальная разработка

- Сборка: `dotnet build src/GalacticExpansion.sln --configuration Release`
- Тесты: `dotnet test src/GalacticExpansion.sln --configuration Release`
- **ВАЖНО:** При NuGet proxy проблемах: `--no-restore`

## Логирование

- NLog, путь к конфигу задается явно в `ModMain`
- **Префиксы процессов**: `[Dedi-PID]` / `[PfServer-PID]` / `[PfServer-Native]`
- Layout: `${longdate}|${level}|${logger}|${gdc:item=process}|${message}`

## State Management (Phase 3.2 — ОБНОВЛЕНО!)

### Принцип: In-memory state = единственный источник правды

- `SimulationEngine._state` — канонический объект SimulationState
- `_stateStore.LoadAsync()` — **ТОЛЬКО при запуске** (StartAsync)
- `_stateStore.SaveAsync()` — автосохранение каждые 60 сек + при shutdown
- **ЗАПРЕЩЕНО** LoadAsync внутри тикового цикла!

### ColonyManager.SetSimulationState()
- Инжектирует ссылку на in-memory state из SimulationEngine
- Вызывается из ModMain после `_simulationEngine.StartAsync()`
- До вызова — fallback на `_stateStore.LoadAsync()` (только при инициализации)

### Конфигурация стадий (из ConfigurationLoader, defaults)
```
ConstructionYard: RequiredResources=0, ProductionRate=100, MinTime=600s
BaseL1: RequiredResources=1000, ProductionRate=150, MinTime=1800s
BaseL2: RequiredResources=3000, ProductionRate=200, MinTime=3600s
BaseL3: RequiredResources=6000, ProductionRate=250, MinTime=7200s
BaseMax: RequiredResources=10000, ProductionRate=300, MinTime=14400s
```

## Multi-Process Architecture

### Dedi процесс:
```
NetworkBridge → IPCEntitySpawner → StageManager → ColonyManager → модули
SimulationEngine (тики) → ColonyTickModule → UpdateColonyAsync
Game_Update → TryMaterializePendingColoniesAsync
```

### PfServer процесс:
```
OnPlayfieldLoaded → IPlayfield → NativePlayfieldSpawner
NetworkBridge → HandleIPCRequestWithNativeSpawner
```

## Известные нюансы

- В тестах использовать `IPlayfieldWrapper` вместо `IPlayfield`
- **UnityEngine.ILogger vs NLog.ILogger:** Добавлять alias при using UnityEngine
- Дефолтные префабы колоний — ванильные (BA_ConstructionSite, BA_Zirax_*)
- **IPC таймауты**: 15s структуры, 10s NPC
