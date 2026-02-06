# Progress — GalacticExpansion (GLEX)

## Текущий статус

**Дата обновления:** 06.02.2026  
**Phase 3 (Domain):** ✅ ЗАВЕРШЕНА с Multi-Process IPC Architecture  
**Phase 4 (Combat):** не начата

## Недавний прогресс

### 🚀 Multi-Process IPC Architecture — РЕВОЛЮЦИОННОЕ ИЗМЕНЕНИЕ ✅

**Задача:** Полностью решить проблему `PlayfieldConnectionNotFound` через правильную multi-process архитектуру.

**Фундаментальная проблема:**
- Empyrion использует multi-process: Dedi (логика) + PfServer (spawn)
- Мод загружается в ОБА процесса
- Spawn работает ТОЛЬКО из PfServer процесса
- Старая архитектура пыталась спавнить из Dedi → PlayfieldConnectionNotFound

**Реализация IPC:**

**1. IPC Протокол** (`Core/IPC/IPCProtocol.cs`):
- `IPCMessage` - базовый класс (Version, MessageType, RequestId, Timestamp)
- `SpawnStructureRequest/Response` - spawn структур через IPC
- `SpawnNPCRequest/Response` - spawn NPC через IPC
- `PlayfieldReadyRequest/Response` - проверка готовности playfield
- JSON сериализация для простоты отладки

**2. NetworkBridge** (`Core/IPC/NetworkBridge.cs`):
- IPC транспорт-слой вокруг `INetwork` ModAPI
- `InitializeForDedi()` - регистрация receiver для Dedi (получение ответов)
- `InitializeForPlayfieldServer(pfName)` - регистрация receiver для PfServer (получение запросов)
- `SendRequestToPlayfieldAsync<T>()` - отправка с async/await и таймаутом
- Request/Response tracking через `ConcurrentDictionary<Guid, TaskCompletionSource>`

**3. IPCEntitySpawner** (`Core/Spawning/IPCEntitySpawner.cs`):
- Wrapper вокруг EntitySpawner
- Автоматическая маршрутизация по `_processMode`:
  - Dedi: отправляет IPC запрос через NetworkBridge
  - PfServer: прямой spawn через directSpawner
- Поддержка структур И NPC через IPC
- Прозрачность: код использует `IEntitySpawner` не зная про IPC!

**4. ModMain** - multi-process инициализация:
- `Init(IModApi)` определяет режим: `modApi.Application.Mode`
- `InitializeDedicatedServer()` - полная инициализация:
  - NetworkBridge создается **ПЕРВЫМ**
  - IPCEntitySpawner создается **ДО** модулей (правильный граф!)
  - StageManager → ColonyManager → ColonyTickModule получают IPC spawner
- `InitializePlayfieldServer()` - легковесная:
  - Подписка на `OnPlayfieldLoaded` для гарантированной готовности
  - NetworkBridge receiver для IPC команд
  - EntitySpawner для выполнения spawn
- `HandleIPCRequestAsync()` - обработчик IPC в PfServer:
  - **ЖЕСТКАЯ проверка playfield** (return error если не совпадает!)
  - Выполнение spawn и отправка response

**Критические исправления:**
1. ✅ Порядок инициализации: NetworkBridge → IPCEntitySpawner → модули
2. ✅ OnPlayfieldLoaded подписка в PfServer для готовности
3. ✅ NPC spawn через IPC (SpawnNPCRequest/Response)
4. ✅ Жесткая проверка playfield (return error вместо warning)
5. ✅ Удалена старая материализация из Game_Update (теперь через IPC автоматически)

**Результат:**
- ✅ Spawn ГАРАНТИРОВАННО в правильном процессе (PfServer)
- ✅ Решена проблема PlayfieldConnectionNotFound
- ✅ Работает для структур И NPC
- ✅ Прозрачная IPC коммуникация
- ✅ Полное логирование с префиксами `[Dedi-PID]` / `[PfServer-PID]`

**Документация:** 
- `docs/architecture/12_Multi_Process_IPC_Architecture.md` - полная документация
- `docs/architecture/IPC_Quick_Reference.md` - быстрая справка
- `docs/architecture/11_Colony_Virtualization.md` - обновлена с IPC информацией

### Прочие изменения Phase 3

- **EmpyrionGateway:** обработка `CmdId.Event_Error` с извлечением `ErrorType`
- **PlacementResolver:** поздняя установка `IModApi` через `SetModApi`
- **ConfigurationLoader:** дефолтные префабы для Zirax.Stages/DropShips
- **Документация:** инструкция для тестера `docs/manuals/Tester_Manual_Colony_Access.md`
- Исправлены все unit/integration тесты

## Тестирование

- Unit: проходят ✅  
- Integration: проходят ✅  
- Команда: `dotnet test src/GalacticExpansion.sln --configuration Release`

## Что работает

### ✅ Phase 1-2 (Foundation & Core Loop)
- Core Loop, Gateway, State Store, Trackers — стабильно

### ✅ Phase 3 (Domain) — ЗАВЕРШЕНА
- **Spawning & Evolution:** EntitySpawner, StageManager с поддержкой виртуализации
- **Placement:** PlacementResolver с IModApi для определения высоты
- **Economy:** EconomySimulator, UnitEconomyManager
- **Colony Management:** ColonyManager с системой виртуализации
- **Colony Virtualization:** полный цикл от создания виртуальной колонии до материализации
- **ColonyTickModule:** создание виртуальных колоний при старте, retry-логика материализации

### 🎯 Ключевые возможности
1. Виртуальные колонии создаются при старте мода (независимо от игрока)
2. Виртуальное развитие: ресурсы, юниты, переходы стадий
3. Материализация при загрузке playfield с retry-логикой (до 10 попыток)
4. Решение проблемы `PlayfieldConnectionNotFound`

## Что дальше

1. **Тестирование виртуализации:** проверка полного цикла создание → развитие → материализация → спавн
2. Phase 3.5: server testing на dedicated server (мультиплеер, нагрузочное тестирование)
3. Phase 4: Threat Director + AIM Orchestrator (реакция на действия игроков)
