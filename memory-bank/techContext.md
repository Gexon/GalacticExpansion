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
  - `Spawning/` - EntitySpawner, IPCEntitySpawner, **NativePlayfieldSpawner**, StageManager
  - `Simulation/` - SimulationEngine, ColonyManager, модули
  - `Gateway/` - EmpyrionGateway для ModAPI
- `src/GalacticExpansion.Models` — модели данных
- `src/GalacticExpansion` — entry point (ModMain)
- `lib/` - Empyrion DLLs: ModApi.dll, Mif.dll, **UnityEngine.CoreModule.dll**

## Локальная разработка

- Сборка: `dotnet build src/GalacticExpansion.sln --configuration Release`
- Тесты: `dotnet test src/GalacticExpansion.sln --configuration Release`
- **ВАЖНО:** При NuGet proxy проблемах: `--no-restore`

## Логирование

- NLog, путь к конфигу задается явно в `ModMain`
- **Префиксы процессов**: `[Dedi-PID]` / `[PfServer-PID]` / `[PfServer-Native]`
- Layout: `${longdate}|${level}|${logger}|${gdc:item=process}|${message}`
- Критично для отладки multi-process проблем

## Multi-Process Architecture (Phase 3.1 — ОБНОВЛЕНО!)

### Ключевые компоненты:

**1. Native Playfield Spawner** (НОВОЕ! - Phase 3.1):
- **Файлы:**
  - `INativePlayfieldSpawner.cs` - интерфейс
  - `NativePlayfieldSpawner.cs` - реализация
  - `UnityTypeConverter.cs` - конвертация типов
- **Назначение:** Прямой спавн через IPlayfield API без ModGameAPI
- **Использование:** Только в PfServer процессе
- **API:**
  - `SpawnStructureAsync()` → `IPlayfield.SpawnPrefab()`
  - `SpawnNPCAsync()` → `IPlayfield.SpawnEntity()`
  - `GetTerrainHeight()` → `IPlayfield.GetTerrainHeightAt()`
- **Преимущества:**
  - НЕ требует ModGameAPI (решает краш PfServer!)
  - Синхронные вызовы (быстрее: ~50-100ms)
  - Прямой доступ к playfield entities
  - Простая архитектура

**2. IPCProtocol** (`GalacticExpansion.Core/IPC/IPCProtocol.cs`):
- `IPCMessage` - базовый класс
- `SpawnStructureRequest/Response`, `SpawnNPCRequest/Response`
- JSON сериализация

**3. NetworkBridge** (`GalacticExpansion.Core/IPC/NetworkBridge.cs`):
- `InitializeForDedi()` / `InitializeForPlayfieldServer(pfName)`
- `SendRequestToPlayfieldAsync<T>()` - отправка с таймаутом
- Tracking через `ConcurrentDictionary<Guid, TaskCompletionSource>`

**4. IPCEntitySpawner** (`GalacticExpansion.Core/Spawning/IPCEntitySpawner.cs`):
- Wrapper вокруг EntitySpawner
- Автоматическая маршрутизация по `_processMode`
- Dedi → IPC, PfServer → direct spawn

### Критические правила:

1. **Порядок инициализации в Dedi:**
   ```
   NetworkBridge → IPCEntitySpawner → StageManager → ColonyManager → модули
   ```

2. **PfServer инициализация (НОВОЕ!):**
   ```
   OnPlayfieldLoaded → IPlayfield instance
       ↓
   NativePlayfieldSpawner(pfInstance)
       ↓
   NetworkBridge.InitializeForPlayfieldServer()
       ↓
   OnRequestReceived → HandleIPCRequestWithNativeSpawner
   ```
   - НЕ создает Gateway/EntitySpawner
   - Только NetworkBridge + NativePlayfieldSpawner

3. **UnityEngine.ILogger конфликт:**
   ```csharp
   using ILogger = NLog.ILogger; // ОБЯЗАТЕЛЬНО при using UnityEngine!
   ```

4. **Зависимости проектов:**
   - `GalacticExpansion.Core.csproj` → UnityEngine.CoreModule.dll
   - `GalacticExpansion.csproj` → UnityEngine.CoreModule.dll

## Известные нюансы

- В тестах использовать `IPlayfieldWrapper` вместо `IPlayfield`.
- **UnityEngine.ILogger vs NLog.ILogger:** Добавлять alias при using UnityEngine
- **IPlayfield API:** Синхронные методы (не Task-based)
- Дефолтные префабы колоний — ванильные (BA_ConstructionSite, BA_Zirax_*)
- **IPC таймауты**: 15s структуры, 10s NPC
- **JSON vs Binary**: JSON для отладки (можно ProtoBuf для оптимизации)
