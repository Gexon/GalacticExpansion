# Tech Context — GalacticExpansion (GLEX)

## Стек

- C# 8.0+, .NET Framework 4.8
- Newtonsoft.Json 13.x
- NLog 5.x
- xUnit 2.6+, Moq 4.20+

## Внешние зависимости (Empyrion)

- `ModApi.dll`, `Mif.dll`, `protobuf-net.dll`

## Ключевые ModAPI возможности

- Спавн/удаление сущностей: `Request_Entity_Spawn`, `Request_Entity_Destroy`
- Список структур: `Request_GlobalStructure_List`
- Защита от decay: `Request_Structure_Touch`
- Высота рельефа: `IPlayfield.GetTerrainHeightAt(x, z)` (через `IPlayfieldWrapper`)
- **IPC коммуникация**: `INetwork.SendToPlayfieldServer()`, `INetwork.SendToDedicatedServer()`
- **Определение процесса**: `IModApi.Application.Mode` (DedicatedServer / PlayfieldServer)
- **Готовность playfield**: `IModApi.Application.OnPlayfieldLoaded` (IPlayfield instance)

## Структура проекта

- `src/GalacticExpansion.Core` — модули
  - `IPC/` - **IPC протокол и NetworkBridge** для multi-process коммуникации
  - `Spawning/` - EntitySpawner, IPCEntitySpawner, StageManager
  - `Simulation/` - SimulationEngine, ColonyManager, модули
  - `Gateway/` - EmpyrionGateway для ModAPI
- `src/GalacticExpansion.Models` — модели данных
- `src/GalacticExpansion` — entry point (ModMain)
- `src/GalacticExpansion.Tests.Unit`, `src/GalacticExpansion.Tests.Integration`

## Локальная разработка

- Сборка: `dotnet build src/GalacticExpansion.sln --configuration Release`
- Тесты: `dotnet test src/GalacticExpansion.sln --configuration Release`

## Логирование

- NLog, путь к конфигу задается явно в `ModMain`
- **Префиксы процессов**: `[Dedi-PID]` / `[PfServer-PID]` через GlobalDiagnosticsContext
- Layout: `${longdate}|${level}|${logger}|${gdc:item=process}|${message}`
- Критично для отладки multi-process проблем

## ModAPI: ответы с ошибкой

- При ошибке запроса игра возвращает `CmdId.Event_Error`, данные — `ErrorInfo` (поле `errorType` типа `ErrorType`). В логах и исключениях используется `errorType.ToString()` (например, `EntityNotLocalToPlayfield`, `PlayfieldConnectionNotFound`). См. `EmpyrionGateway.HandleEvent` и `GetErrorMessageFromErrorInfo`.

## ModAPI: загрузка playfield

- Событие `CmdId.Event_Playfield_Loaded` использует тип данных `PlayfieldLoad` (из `Mif.dll` / `Eleon.Modding`), содержащий:
  - `sec` — время загрузки playfield в секундах,
  - `playfield` — имя загруженного playfield,
  - `processId` — ID процесса/инстанса.
- В `ColonyTickModule` данные события декодируются как `PlayfieldLoad`, имя playfield берётся из поля `playfield` и используется для:
  - отложенного создания первой колонии на `HomePlayfield` (Phase 3.5, первая физическая база появляется только после загрузки нужного playfield),
  - вызова `IColonyManager.EnsurePlayfieldColoniesSpawnedAsync(playfield)` для обновления/защиты структур колоний на этом playfield.

## Multi-Process IPC Architecture (КРИТИЧНО!)

### Ключевые компоненты:

**1. IPCProtocol** (`GalacticExpansion.Core/IPC/IPCProtocol.cs`):
- `IPCMessage` - базовый класс (Version, MessageType, RequestId, Timestamp)
- `SpawnStructureRequest/Response` - spawn структур
- `SpawnNPCRequest/Response` - spawn NPC
- `PlayfieldReadyRequest/Response` - проверка готовности
- JSON сериализация (Newtonsoft.Json)

**2. NetworkBridge** (`GalacticExpansion.Core/IPC/NetworkBridge.cs`):
- `InitializeForDedi()` - регистрация `RegisterReceiverForDediPackets`
- `InitializeForPlayfieldServer(pfName)` - регистрация `RegisterReceiverForPlayfieldPackets`
- `SendRequestToPlayfieldAsync<T>(request, pfName, timeout)` - отправка с ожиданием ответа
- Tracking pending requests через `ConcurrentDictionary<Guid, TaskCompletionSource>`
- Event handler: `OnRequestReceived` - обрабатывается в ModMain.HandleIPCRequestAsync

**3. IPCEntitySpawner** (`GalacticExpansion.Core/Spawning/IPCEntitySpawner.cs`):
- Wrapper вокруг EntitySpawner
- Проверяет `_processMode` и выбирает:
  - `DedicatedServer` → `SpawnViaIPCAsync()` → NetworkBridge → PfServer
  - `PlayfieldServer` → `directSpawner.SpawnStructureAsync()` → Request_Entity_Spawn
- Поддержка структур И NPC через IPC
- Единый интерфейс `IEntitySpawner` для всей кодовой базы

**4. EntitySpawnInfo (исправления):**
- `playfield` - ОБЯЗАТЕЛЬНО заполняется (название playfield)
- `factionId` - int (не byte!)
- `factionGroup` - НЕ используется (только если factionId = -1)
- Для NPC: `entityTypeName` вместо `prefabName` и `type`

### Критические правила:

1. **Порядок инициализации в Dedi:**
   ```
   NetworkBridge (ПЕРВЫЙ!) → IPCEntitySpawner → StageManager → ColonyManager → модули
   ```
   Если создать модули ДО IPCEntitySpawner → они получат старый EntitySpawner → spawn в неправильном процессе!

2. **Готовность PfServer:**
   - Подписка на `OnPlayfieldLoaded` для получения `IPlayfield` instance
   - `_currentPlayfield = pfInstance.Name` - сохраняем название
   - `NetworkBridge.InitializeForPlayfieldServer(_currentPlayfield)` - завершаем инициализацию
   - Race condition защита: IPC НЕ сработает если NetworkBridge не инициализирован!

3. **Жесткая проверка playfield:**
   ```csharp
   if (spawnReq.Playfield != _currentPlayfield)
       return Error("Playfield mismatch!");
   ```
   Если не проверять → spawn в неправильном playfield → PlayfieldConnectionNotFound!

4. **Throttling в Game_Update:**
   - Dedi: периодические бэкапы каждые N часов
   - Материализация теперь АВТОМАТИЧЕСКАЯ через IPC (не нужно вызывать в Game_Update!)
   - PfServer: минимальная логика (IPC async)

### IPC поток данных:

```
Dedi: ColonyManager → StageManager → IPCEntitySpawner → SpawnViaIPCAsync()
  ↓
NetworkBridge.SendRequestToPlayfieldAsync() → JSON → INetwork.SendToPlayfieldServer()
  ↓
[IPC через сетевой слой Empyrion]
  ↓
PfServer: INetwork → NetworkBridge.OnPlayfieldPacketReceived() → HandleIPCRequestAsync()
  ↓
Проверка playfield → IPCEntitySpawner → directSpawner → Request_Entity_Spawn
  ↓
✅ Entity spawned → SpawnStructureResponse → NetworkBridge → обратно в Dedi
  ↓
Dedi: TaskCompletionSource.SetResult() → SpawnViaIPCAsync() возвращает EntityId
```

## Известные нюансы

- В тестах использовать `IPlayfieldWrapper` вместо `IPlayfield`.
- Контракты интерфейсов должны совпадать с реализациями (порядок параметров важен).
- `IPlacementResolver.SetModApi(IModApi?)` вызывается в ModMain.Init для поздней инъекции ModApi (точная высота рельефа).
- Дефолтные префабы колоний — ванильные (BA_ConstructionSite, BA_Zirax_*, BA_MiningOutpost_Zirax_1); задаются в ConfigurationLoader при отсутствии в конфиге.
- **IPC таймауты**: 15s для структур, 10s для NPC (настраивается в IPCEntitySpawner)
- **JSON vs Binary**: Используем JSON для простоты отладки (можно заменить на ProtoBuf если нужна оптимизация)
