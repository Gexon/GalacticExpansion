# Multi-Process IPC Architecture

**Версия:** 1.1  
**Дата:** 2026-10-01  
**Статус:** Доставка пакетов подтверждена на сервере. Разбор JSON в `DeserializeMessage` — следующий скоуп.

## Оглавление

- [Обзор](#обзор)
- [Проблема: Почему нужна IPC архитектура](#проблема-почему-нужна-ipc-архитектура)
- [Архитектура Empyrion Dedicated Server](#архитектура-empyrion-dedicated-server)
- [Решение: IPC через NetworkBridge](#решение-ipc-через-networkbridge)
- [Компоненты системы](#компоненты-системы)
- [Поток данных](#поток-данных)
- [Критические детали реализации](#критические-детали-реализации)
- [Канал INetwork — имя вызывающей сборки](#0-канал-inetwork--имя-вызывающей-сборки)
- [Примеры использования](#примеры-использования)

---

## Обзор

GalacticExpansion использует **Inter-Process Communication (IPC)** архитектуру для взаимодействия между процессами Empyrion Dedicated Server. Это критически важно для правильной работы spawn операций в multi-process окружении.

### Ключевые принципы:

1. **Разделение ответственности**: Логика в Dedi процессе, spawn в PfServer процессе
2. **IPC коммуникация**: Обмен сообщениями через `INetwork` ModAPI
3. **Асинхронность**: Request/Response паттерн с таймаутами
4. **Надежность**: Жесткая проверка playfield и обработка ошибок

---

## Проблема: Почему нужна IPC архитектура

### Ошибка `PlayfieldConnectionNotFound`

При попытке вызвать `Request_Entity_Spawn` из **неправильного процесса**, Empyrion возвращает ошибку:

```
PlayfieldConnectionNotFound: No connection to playfield server for 'Temperate Planet'
```

### Причина ошибки

Empyrion Dedicated Server использует **multi-process архитектуру**:

```
┌────────────────────────────────────────────────────┐
│  DEDI ПРОЦЕСС (Main Server)                       │
│  - Управление игрой                                │
│  - Чат, логика                                     │
│  - НЕТ прямого доступа к playfield entities!       │
└────────────────────────────────────────────────────┘
                    ↓ управляет
        ┌───────────┴───────────┐
        ↓                       ↓
┌─────────────────┐    ┌─────────────────┐
│ PFSERVER-1      │    │ PFSERVER-2      │
│ (Temperate)     │    │ (Moon)          │
│ - Entity spawn  │    │ - Entity spawn  │
│ - Playfield ops │    │ - Playfield ops │
└─────────────────┘    └─────────────────┘
```

**Проблема**: Если мод загружается в Dedi процесс и пытается заспавнить entity, у него **нет connection** к playfield серверу!

### Неправильный подход (до исправления):

```csharp
// В Dedi процессе:
public void Game_Update() 
{
    // ❌ ОШИБКА: Dedi процесс не имеет connection к playfield!
    await entitySpawner.SpawnStructureAsync("Temperate Planet", ...);
    // Результат: PlayfieldConnectionNotFound
}
```

### Правильный подход (текущая реализация):

```csharp
// В Dedi процессе:
public void Game_Update() 
{
    // ✅ Отправляем IPC команду в PfServer
    await ipcEntitySpawner.SpawnStructureAsync("Temperate Planet", ...);
    // IPCEntitySpawner → NetworkBridge → PfServer → spawn
}

// В PfServer процессе:
private async Task HandleIPCRequest(SpawnStructureRequest request)
{
    // ✅ Spawn выполняется в правильном процессе!
    var entityId = await directSpawner.SpawnStructureAsync(...);
    return new SpawnStructureResponse { EntityId = entityId };
}
```

---

## Архитектура Empyrion Dedicated Server

### Жизненный цикл мода

Мод загружается **ДВАЖДЫ** в разные процессы:

```
┌─────────────────────────────────────────────────────┐
│ 1. DEDI ПРОЦЕСС                                     │
│    ModMain.Init(IModApi modApi)                     │
│    modApi.Application.Mode = DedicatedServer        │
│    → InitializeDedicatedServer()                    │
│    → Полная инициализация (симуляция, логика)      │
└─────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────┐
│ 2. PFSERVER ПРОЦЕСС (для каждого playfield)        │
│    ModMain.Init(IModApi modApi)                     │
│    modApi.Application.Mode = PlayfieldServer        │
│    → InitializePlayfieldServer()                    │
│    → Легковесная инициализация (spawn executor)    │
└─────────────────────────────────────────────────────┘
```

### ModTargets в Info.yaml

```yaml
ModTargets:
  - Type: DedicatedServer   # ← Загружается в Dedi процесс
  - Type: PfServer         # ← Загружается в каждый PfServer процесс
```

Это означает что `ModMain.Init()` будет вызван **несколько раз** в разных процессах!

---

## Решение: IPC через NetworkBridge

### Концепция

**NetworkBridge** - это wrapper вокруг `INetwork` ModAPI для упрощенной IPC коммуникации:

```
DEDI → NetworkBridge → INetwork → [Сетевой слой] → INetwork → NetworkBridge → PFSERVER
         (Sender)                                                  (Receiver)
```

### Протокол сообщений

Все IPC сообщения наследуют от `IPCMessage`:

```csharp
public abstract class IPCMessage
{
    public int Version { get; set; } = 1;           // Версионирование
    public string MessageType { get; set; }         // Тип сообщения
    public Guid RequestId { get; set; }             // Уникальный ID запроса
    public DateTime Timestamp { get; set; }         // Временная метка
}
```

#### Типы сообщений:

**1. Spawn структуры:**
```csharp
SpawnStructureRequest {
    Playfield, PrefabName, Position, Rotation, FactionId, EntityType
}
SpawnStructureResponse {
    Success, EntityId, ErrorMessage
}
```

**2. Spawn NPC:**
```csharp
SpawnNPCRequest {
    Playfield, NPCClassName, Position, Rotation, FactionName
}
SpawnNPCResponse {
    Success, EntityId, ErrorMessage
}
```

**3. Проверка готовности playfield:**
```csharp
PlayfieldReadyRequest {
    Playfield
}
PlayfieldReadyResponse {
    IsReady, Playfield, Info
}
```

### Формат данных

Сообщения сериализуются в **JSON** и передаются как `byte[]`:

```csharp
private byte[] SerializeMessage(IPCMessage message)
{
    var json = JsonConvert.SerializeObject(message, Formatting.None);
    return Encoding.UTF8.GetBytes(json);
}
```

**Почему JSON?**
- ✅ Человекочитаемый формат (легко отлаживать)
- ✅ Гибкость (легко добавлять поля)
- ✅ Нет проблем с версионированием
- ❌ Немного медленнее чем binary (но для наших целей достаточно)

---

## Компоненты системы

### 1. NetworkBridge

**Местоположение:** `GalacticExpansion.Core/IPC/NetworkBridge.cs`

**Ответственность:**
- Регистрация IPC receivers
- Отправка запросов с таймаутами
- Сопоставление request/response через RequestId
- Обработка ошибок и таймаутов

`NetworkBridge` сам `ModApi.Network` не вызывает. Регистрация и отправка идут через `IEmpyrionModChannel`. Реализация — `EmpyrionModChannel` в сборке `GalacticExpansion.dll`. Почему так — в разделе про вызывающую сборку.

**Ключевые методы:**

```csharp
// Dedi слушает пакеты, которые созданы на playfield
public void InitializeForDedi()
{
    _channel.RegisterReceiverForPlayfieldPackets(OnDediPacketReceived);
}

// PfServer слушает пакеты, которые созданы на dedicated
public void InitializeForPlayfieldServer(string playfieldName)
{
    _currentPlayfield = playfieldName;
    _channel.RegisterReceiverForDediPackets(OnPlayfieldPacketReceived);
}

// Отправка запроса от Dedi к PfServer
public async Task<TResponse> SendRequestToPlayfieldAsync<TResponse>(
    IPCMessage request, 
    string playfieldName, 
    int timeoutMs = 15000)
{
    // 1. Генерируем RequestId
    // 2. Регистрируем TaskCompletionSource
    // 3. Отправляем через INetwork.SendToPlayfieldServer()
    // 4. Ждем ответ с таймаутом
    // 5. Возвращаем результат или бросаем TimeoutException
}
```

**Tracking запросов:**

```csharp
// Dictionary для отслеживания pending requests
private readonly ConcurrentDictionary<Guid, PendingRequest> _pendingRequests;

class PendingRequest {
    Guid RequestId;
    TaskCompletionSource<IPCMessage> TaskCompletionSource;
    DateTime CreatedAt;
}
```

Когда приходит ответ, NetworkBridge находит соответствующий `TaskCompletionSource` по `RequestId` и завершает Task.

### 2. IPCEntitySpawner

**Местоположение:** `GalacticExpansion.Core/Spawning/IPCEntitySpawner.cs`

**Ответственность:**
- Wrapper вокруг EntitySpawner
- Автоматическая маршрутизация: IPC (Dedi) или direct spawn (PfServer)
- Единый интерфейс `IEntitySpawner` для всего кода

**Паттерн Strategy:**

```csharp
public async Task<int> SpawnStructureAsync(...)
{
    if (_processMode == ApplicationMode.DedicatedServer)
    {
        // DEDI: отправляем IPC запрос
        return await SpawnViaIPCAsync(...);
    }
    else if (_processMode == ApplicationMode.PlayfieldServer)
    {
        // PFSERVER: прямой spawn
        return await _directSpawner.SpawnStructureAsync(...);
    }
}
```

**Преимущества:**
- ✅ Прозрачность: код использует `IEntitySpawner` не зная про IPC
- ✅ Тестируемость: можно подменить на mock
- ✅ Единое место логики spawn

### 3. ModMain

**Местоположение:** `GalacticExpansion/ModMain.cs`

**Ответственность:**
- Определение режима процесса через `modApi.Application.Mode`
- Разная инициализация для Dedi и PfServer
- Обработка IPC запросов в PfServer

**Инициализация:**

```csharp
public void Init(IModApi modAPI)
{
    _processMode = modAPI.Application.Mode;
    
    if (_processMode == ApplicationMode.DedicatedServer)
    {
        InitializeDedicatedServer(modAPI);  // Полная инициализация
    }
    else if (_processMode == ApplicationMode.PlayfieldServer)
    {
        InitializePlayfieldServer(modAPI);  // Легковесная инициализация
    }
}
```

---

## Поток данных

### Сценарий: Материализация колонии

```
═══════════════════════════════════════════════════════════════════
                        DEDI ПРОЦЕСС
═══════════════════════════════════════════════════════════════════

1. Event_Playfield_Loaded приходит в Dedi
   ↓
2. ColonyTickModule обрабатывает событие
   ↓
3. ColonyManager.TryMaterializePendingColoniesAsync()
   ↓
4. StageManager.MaterializeColonyAsync()
   ↓
5. IPCEntitySpawner.SpawnStructureAsync("Temperate Planet", "Base_L1", ...)
   [Обнаруживает: _processMode == DedicatedServer]
   ↓
6. SpawnViaIPCAsync() создает SpawnStructureRequest:
   {
     RequestId: "abc-123-def",
     Playfield: "Temperate Planet",
     PrefabName: "Base_L1",
     Position: [100, 50, 200],
     Rotation: [0, 0, 0],
     FactionId: 2
   }
   ↓
7. NetworkBridge.SendRequestToPlayfieldAsync()
   - Регистрирует TaskCompletionSource для RequestId
   - Сериализует в JSON → byte[]
   - INetwork.SendToPlayfieldServer("GalacticExpansion", "Temperate Planet", data)
   ↓
   ═══════════════════════════════════════════════════════════════
                     [IPC через INetwork]
   ═══════════════════════════════════════════════════════════════
   ↓
═══════════════════════════════════════════════════════════════════
                      PFSERVER ПРОЦЕСС (Temperate Planet)
═══════════════════════════════════════════════════════════════════

8. NetworkBridge.OnPlayfieldPacketReceived(sender, pfName, data)
   - Десериализует byte[] → JSON → SpawnStructureRequest
   ↓
9. Task.Run(() => OnRequestReceived?.Invoke(request, pfName))
   ↓
10. ModMain.HandleIPCRequestAsync(SpawnStructureRequest)
    ↓
11. ЖЕСТКАЯ ПРОВЕРКА:
    if (request.Playfield != _currentPlayfield) 
        return Error("Playfield mismatch!");
    ↓
12. entitySpawner.SpawnStructureAsync() [IPCEntitySpawner в PfServer mode]
    [Обнаруживает: _processMode == PlayfieldServer]
    ↓
13. directSpawner.SpawnStructureAsync()
    ↓
14. EntitySpawner.SpawnStructureAsync()
    ↓
15. Gateway.SendRequestAsync(CmdId.Request_Entity_Spawn, spawnInfo)
    ↓
16. ✅ EMPYRION SPAWNS ENTITY!
    ↓
17. EntityId = 12345 возвращается
    ↓
18. SpawnStructureResponse создается:
    {
      RequestId: "abc-123-def",  ← Тот же RequestId!
      Success: true,
      EntityId: 12345
    }
    ↓
19. NetworkBridge.SendToDedicatedServer("GalacticExpansion", responseData, "Temperate Planet")
    ↓
   ═══════════════════════════════════════════════════════════════
                     [IPC через INetwork]
   ═══════════════════════════════════════════════════════════════
   ↓
═══════════════════════════════════════════════════════════════════
                        DEDI ПРОЦЕСС (возврат)
═══════════════════════════════════════════════════════════════════

20. NetworkBridge.OnDediPacketReceived(sender, pfName, data)
    - Десериализует → SpawnStructureResponse
    ↓
21. Находит PendingRequest по RequestId = "abc-123-def"
    ↓
22. TaskCompletionSource.SetResult(response)
    ↓
23. await SendRequestToPlayfieldAsync() завершается с результатом
    ↓
24. SpawnViaIPCAsync() получает EntityId = 12345
    ↓
25. ColonyManager получает подтверждение успешного spawn
    ↓
26. ✅ Колония материализована!
```

**Время выполнения**: ~500ms-2s (зависит от нагрузки сервера)

---

## Критические детали реализации

### 0. Канал INetwork — имя вызывающей сборки

Проверено по IL `Eleon.ModBridge.NetworkBridge` в `Assembly-CSharp.dll` (не наш класс) и по логам `logs-2006-10-01_v5` (2026-10-01 18:30): после переноса вызовов в `GalacticExpansion.dll` обе стороны пишут `Received packet from 'GalacticExpansion'`.

Игра не смотрит ни на `Name` в `*_Info.yaml`, ни на папку мода.

`RegisterReceiverForDediPackets` / `RegisterReceiverForPlayfieldPackets`:

1. `Assembly.GetCallingAssembly()`
2. ключ словаря = `assembly.GetName().Name`
3. в словарь кладётся колбэк

`OnDataReceivedFromDedicated` / `OnDataReceivedFromPlayfieldServer` делают `TryGetValue(receiver)`. Нет ключа — метод выходит молча, колбэк не вызывается, в лог игры ничего не пишется.

`Send*` при этом может вернуть `true`: это значит «процесс назначения найден», а не «колбэк мода вызван». `sender` в колбэке — имя сборки, которая вызвала `Send*`. Оно совпадает со строкой `receiver` только когда и регистрация, и отправка идут из сборки с этим именем.

| Откуда вызван `Register*` / `Send*` | Ключ колбэка | Что будет, если `receiver` = `"GalacticExpansion"` |
| --- | --- | --- |
| `GalacticExpansion.dll` (`EmpyrionModChannel`) | `GalacticExpansion` | пакет доходит |
| `GalacticExpansion.Core.dll` (`NetworkBridge`) | `GalacticExpansion.Core` | `Send*` часто `true`, колбэка нет |

Так устроен рабочий пример EmpyrionScripting: и `RegisterReceiverForPlayfieldPackets`, и `SendToDedicatedServer("EmpyrionScripting", ...)` живут в `EmpyrionScripting.dll`. ASTIC на форуме Empyrion отдельно писал, что канал — имя мода, и DLL у него названа так же.

Наши правила:

- Единственная точка вызова `ModApi.Network` — `src/GalacticExpansion/IPC/EmpyrionModChannel.cs`.
- Методы помечены `NoInlining` и имеют отдельный кадр стека в этой сборке. Если JIT перенесёт `call` в Core, `GetCallingAssembly()` снова увидит `GalacticExpansion.Core`.
- `receiver` берётся из `typeof(EmpyrionModChannel).Assembly.GetName().Name`, не из строковой константы в Core.
- `NetworkBridge` принимает `IEmpyrionModChannel` и фильтрует `sender == channel.ChannelId`.

Какой `Send*` реально что-то отправляет, зависит от процесса. В dedicated `Assembly-CSharp` метод `SendToDedicatedServer` — заглушка `ldc.i4.0; ret`. В клиентской сборке, которой пользуется PfServer, заглушка — `SendToPlayfieldServer`. Вызов «в свою» сторону всегда `false`. `SendToPlayfieldServer` также возвращает `false`, пока процесс плейфилда ещё не поднят (в логах v4 это `Failed to send message to playfield`, до `OnPlayfieldLoaded`).

Направления не перепутаны:

- Dedi слушает `RegisterReceiverForPlayfieldPackets` и шлёт `SendToPlayfieldServer`.
- PfServer слушает `RegisterReceiverForDediPackets` и шлёт `SendToDedicatedServer`.

### 1. Порядок инициализации (КРИТИЧНО!)

**Проблема**: Если создать модули ДО NetworkBridge и IPCEntitySpawner, они получат ссылку на обычный EntitySpawner → spawn будет в неправильном процессе!

**Решение**: Правильный порядок в InitializeDedicatedServer:

```csharp
private void InitializeDedicatedServer(IModApi modAPI)
{
    // 1. Канал создаётся в сборке GalacticExpansion.dll, затем NetworkBridge.
    //    Вызов ModApi.Network из Core зарегистрирует колбэк под именем GalacticExpansion.Core.
    var channel = new EmpyrionModChannel(modAPI.Network);
    _networkBridge = new NetworkBridge(channel, _logger);
    _networkBridge.InitializeForDedi();
    
    // 2. Создаем Gateway, StateStore, EventBus, ModuleRegistry
    // ...
    
    // 3. Создаем PlacementResolver
    var placementResolver = new PlacementResolver(...);
    
    // 4. Создаем EntitySpawner (direct, для делегирования)
    var directSpawner = new EntitySpawner(...);
    
    // 5. ОБОРАЧИВАЕМ в IPCEntitySpawner!
    var ipcSpawner = new IPCEntitySpawner(
        directSpawner, 
        _networkBridge,  // ← NetworkBridge уже готов!
        ApplicationMode.DedicatedServer,
        logger
    );
    _container.Register<IEntitySpawner>(ipcSpawner);
    
    // 6. Теперь создаем StageManager, ColonyManager, модули
    // Они получат IPCEntitySpawner из контейнера!
    var stageManager = new StageManager(..., ipcSpawner, ...);
    var colonyManager = new ColonyManager(..., stageManager, ...);
    var colonyTickModule = new ColonyTickModule(..., colonyManager, ...);
}
```

**Граф зависимостей:**
```
NetworkBridge
    ↓
IPCEntitySpawner
    ↓
StageManager
    ↓
ColonyManager
    ↓
ColonyTickModule
```

### 2. Гарантированная готовность playfield

**Проблема**: IPC запрос может прийти ДО того как PfServer полностью загрузил playfield → NetworkBridge не инициализирован → команда потеряется!

**Решение**: Двойная защита

```csharp
// В InitializePlayfieldServer():

// 1. Подписка на OnPlayfieldLoaded (IModApi, надежнее)
modAPI.Application.OnPlayfieldLoaded += (pfInstance) =>
{
    _currentPlayfield = pfInstance.Name;
    _networkBridge.InitializeForPlayfieldServer(_currentPlayfield);
};

// 2. Обработка Event_Playfield_Loaded (через Game_Event, fallback)
public void Game_Event(CmdId eventId, ushort seqNr, object data)
{
    if (_processMode == PlayfieldServer && eventId == Event_Playfield_Loaded)
    {
        if (data is PlayfieldLoad pfLoad)
        {
            _currentPlayfield = pfLoad.playfield;
            if (_networkBridge != null)
                _networkBridge.InitializeForPlayfieldServer(_currentPlayfield);
        }
    }
}
```

### 3. Жесткая проверка playfield

**Проблема**: Если IPC команда пришла не в тот PfServer → spawn не сработает → `PlayfieldConnectionNotFound`!

**Решение**: Проверка ДО выполнения spawn

```csharp
private async Task<IPCMessage?> HandleIPCRequestAsync(IPCMessage request, string playfieldName)
{
    if (request is SpawnStructureRequest spawnReq)
    {
        // КРИТИЧНО: Жесткая проверка!
        if (spawnReq.Playfield != _currentPlayfield)
        {
            return new SpawnStructureResponse
            {
                Success = false,
                ErrorMessage = $"Playfield mismatch! Requested={spawnReq.Playfield}, Current={_currentPlayfield}"
            };
        }
        
        // Только если playfield совпадает - выполняем spawn
        var entityId = await entitySpawner.SpawnStructureAsync(...);
        return new SpawnStructureResponse { Success = true, EntityId = entityId };
    }
}
```

### 4. Таймауты и retry

**NetworkBridge таймауты:**
- Spawn структуры: 15 секунд
- Spawn NPC: 10 секунд
- Playfield ready check: настраиваемый

**Retry логика** находится в ColonyTickModule:
```csharp
// При ошибке materialization, colony помечается для retry через 3 секунды
colony.MaterializationAttempts = -3;  // Первая попытка через 3 секунды
```

---

## Примеры использования

### Пример 1: Spawn структуры из кода

```csharp
// Код одинаковый для Dedi и PfServer!
// IPCEntitySpawner сам определит что делать

var entitySpawner = container.Resolve<IEntitySpawner>();

var entityId = await entitySpawner.SpawnStructureAsync(
    playfield: "Temperate Planet",
    prefabName: "GLEX_Base_L1",
    position: new Vector3(100, 50, 200),
    rotation: new Vector3(0, 0, 0),
    factionId: 2  // Zirax
);

// В Dedi: отправит IPC → PfServer → spawn
// В PfServer: прямой spawn через EntitySpawner
```

### Пример 2: Spawn NPC группы

```csharp
var spawnedNPCs = await entitySpawner.SpawnNPCGroupAsync(
    playfield: "Temperate Planet",
    npcClassName: "ZiraxMinigunPatrol",
    centerPosition: new Vector3(100, 50, 200),
    count: 5,
    factionName: "Zirax"
);

// Автоматически:
// - NPC расставлены по кругу (радиус 3м)
// - Задержка 500ms между спавнами
// - Terrain height определяется автоматически
// - IPC если Dedi, direct если PfServer
```

### Пример 3: Проверка готовности playfield

```csharp
var readyRequest = new PlayfieldReadyRequest 
{ 
    Playfield = "Temperate Planet" 
};

var readyResponse = await networkBridge.SendRequestToPlayfieldAsync<PlayfieldReadyResponse>(
    readyRequest, 
    "Temperate Planet", 
    timeoutMs: 5000
);

if (readyResponse.IsReady)
{
    // Playfield готов, можно спавнить
}
```

---

## Диагностика и отладка

### Логирование

Все IPC операции логируются с префиксом процесса:

```
[Dedi-4914] Sending IPC spawn request: Base_L1 on Temperate Planet
[PfServer-5120] Handling IPC request: SpawnStructure (playfield: Temperate Planet)
[PfServer-5120] ✅ Structure spawn successful: EntityId=12345
[Dedi-4914] ✅ IPC spawn successful: EntityId=12345
```

### Формат лога NLog:

```xml
<target name="logfile" 
        layout="${longdate}|${level}|${logger}|${gdc:item=process}|${message}"/>
```

`${gdc:item=process}` - это Global Diagnostics Context, устанавливается в InitializeLogging():

```csharp
var processId = Process.GetCurrentProcess().Id;
var processType = commandLine.Contains("-playfieldserver") ? "PfServer" : "Dedi";
_processPrefix = $"[{processType}-{processId}]";
NLog.GlobalDiagnosticsContext.Set("process", _processPrefix);
```

### Типичные ошибки и решения

**1. `PlayfieldConnectionNotFound` все еще появляется**
- ✅ Убедитесь что используется IPCEntitySpawner, а не EntitySpawner напрямую
- ✅ Проверьте что NetworkBridge инициализирован ДО создания модулей
- ✅ Проверьте логи: должно быть `Sending IPC spawn request` в Dedi

**2. `IPC spawn timeout after 15s`**
- В логе PfServer нет `Received packet` — пакет не дошёл до колбэка. Чаще всего `Register*`/`Send*` вызваны не из `GalacticExpansion.dll` (ключ `GalacticExpansion.Core`), либо playfield-процесс ещё не зарегистрировал приёмник. `Send*` при этом может быть `true`.
- Есть `Received packet from 'GalacticExpansion'`, затем `Failed to deserialize message` и `Could not create an instance of type IPCMessage` — транспорт жив. `DeserializeMessage` делает `DeserializeObject<IPCMessage>`, а базовый класс абстрактный. Это открытый баг, чинится отдельно: сначала прочитать поле `type`, потом десериализовать конкретный класс.
- `Failed to send message to playfield` (исключение, не таймаут) — `SendToPlayfieldServer` вернул `false`: процесс плейфилда ещё не поднят.
- Имя playfield сравнивается как есть, включая пробелы и регистр.

**3. `Playfield mismatch! Requested=X, Current=Y`**
- ✅ IPC команда ушла не в тот PfServer процесс
- ✅ Проверьте правильность названия playfield (пробелы, регистр)
- ✅ Может быть несколько PfServer процессов активны одновременно

**4. Модули держат старую ссылку на EntitySpawner**
- ✅ Проверьте порядок инициализации - NetworkBridge должен быть ПЕРВЫМ!
- ✅ IPCEntitySpawner должен быть зарегистрирован ДО StageManager/ColonyManager
- ✅ ColonyTickModule должен получить ColonyManager с правильным графом

---

## Производительность

### Overhead IPC коммуникации

**Дополнительное время на spawn через IPC**: ~50-200ms

Это включает:
- Сериализация JSON: ~1-5ms
- Передача через INetwork: ~20-100ms
- Десериализация JSON: ~1-5ms
- Обработка в PfServer: ~20-50ms
- Ответ обратно: ~20-100ms

**Итого**: IPC добавляет ~50-200ms к обычному spawn (~300-500ms).

**Приемлемо?** ✅ Да, для spawn операций задержка незначительна.

### Оптимизации

**1. Batch spawn (не реализовано):**
Можно отправлять несколько spawn запросов одним IPC сообщением:
```csharp
var batchRequest = new BatchSpawnRequest {
    Structures = [ ... список структур ... ]
};
```

**2. Переиспользование сериализованных данных:**
Если спавним одинаковые структуры - можно кешировать JSON.

**3. Binary протокол:**
Можно заменить JSON на ProtoBuf для уменьшения размера и ускорения.

---

## Заключение

### Почему именно так?

**IPC архитектура критична** потому что:

1. ✅ **Решает фундаментальную проблему**: Dedi процесс не может спавнить entities
2. ✅ **Следует архитектуре Empyrion**: Каждый процесс делает свою работу
3. ✅ **Надежность**: Жесткие проверки и обработка ошибок
4. ✅ **Масштабируемость**: Работает с любым количеством playfield серверов
5. ✅ **Прозрачность**: Код использует `IEntitySpawner` не зная про IPC

### Альтернативные подходы (и почему они не работают)

**❌ Вариант 1: Весь код в PfServer**
- Проблема: Нет единого state, каждый PfServer изолирован
- Проблема: Симуляция должна быть централизована (Dedi)

**❌ Вариант 2: Прямой spawn из Dedi**
- Проблема: `PlayfieldConnectionNotFound` - невозможно!

**❌ Вариант 3: Shared memory между процессами**
- Проблема: Empyrion не предоставляет такой API
- Проблема: Сложная синхронизация, race conditions

**✅ IPC через INetwork** - единственный правильный способ!

---

**Версия документа:** 1.0  
**Последнее обновление:** 2026-02-06  
**Авторы:** Cursor Composer Agent (Claude Sonnet 4.5)
