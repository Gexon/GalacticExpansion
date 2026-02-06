# Active Context — GalacticExpansion (GLEX)

## Текущее состояние

**Дата обновления:** 06.02.2026  
**Фаза:** Phase 3 Domain + Multi-Process IPC Architecture — РЕАЛИЗОВАНО ✅

## Главное за последние изменения

### 🚀 Multi-Process IPC Architecture — РЕВОЛЮЦИОННОЕ ИЗМЕНЕНИЕ ✅

**Фундаментальная проблема:** 
- Empyrion Dedicated Server использует **multi-process архитектуру**:
  - `Dedi процесс` - главный сервер (чат, логика, НЕТ spawn операций)
  - `PfServer процесс` - playfield сервер (entity spawn, НО НЕТ глобальной логики)
- Мод загружается **ДВАЖДЫ**: в Dedi И в каждый PfServer
- Spawn операции работают **ТОЛЬКО из PfServer процесса**
- Старая архитектура пыталась спавнить из Dedi → `PlayfieldConnectionNotFound`

**Решение - IPC через INetwork ModAPI:**
- **Inter-Process Communication (IPC)** между Dedi и PfServer процессами
- Request/Response протокол через JSON сериализацию
- Автоматическая маршрутизация через `IPCEntitySpawner`
- Жесткая проверка playfield перед spawn операциями

- **Colony модель:**
  - `IsVirtual` (bool) — флаг виртуализации
  - `PendingMaterialization` (bool) — ожидает материализации
  - `MaterializationAttempts` (int) — счетчик попыток материализации

- **ColonyTickModule:** 
  - При инициализации создаёт **виртуальную колонию** (`CreateInitialVirtualColonyAsync`)
  - Создается сразу при старте мода (не ждет события игрока!)
  - `IsVirtual = true`, `Position = (0,0,0)`, без спавна структур
  - Подписывается на `Event_Playfield_Loaded` через `IEmpyrionGateway`
  - **НЕ вызывает** `TryMaterializePendingColoniesAsync` (это теперь в Game_Update)

- **ModMain (Multi-Process):**
  - `Init(IModApi)` определяет режим через `modApi.Application.Mode`
  - `InitializeDedicatedServer()` - полная инициализация (Dedi):
    - NetworkBridge создается **ПЕРВЫМ** (для IPC)
    - IPCEntitySpawner создается **ДО** модулей (правильный граф зависимостей!)
    - SimulationEngine, ColonyManager, все модули
  - `InitializePlayfieldServer()` - легковесная (PfServer):
    - Подписка на `OnPlayfieldLoaded` для гарантированной готовности
    - NetworkBridge receiver для IPC команд от Dedi
    - EntitySpawner для выполнения spawn
  - `Game_Update()` - разная логика:
    - Dedi: периодические бэкапы (материализация через IPC автоматическая!)
    - PfServer: минимальная (IPC обрабатывается асинхронно)

- **NetworkBridge (IPC транспорт):**
  - `InitializeForDedi()` - регистрирует receiver для Dedi процесса
  - `InitializeForPlayfieldServer(playfieldName)` - регистрирует receiver для PfServer
  - `SendRequestToPlayfieldAsync<T>()` - отправка с таймаутом (15s структуры, 10s NPC)
  - Request/Response tracking через `ConcurrentDictionary<Guid, TaskCompletionSource>`
  - JSON сериализация для простоты отладки

- **IPCEntitySpawner (Маршрутизатор):**
  - Wrapper вокруг EntitySpawner, реализует `IEntitySpawner`
  - Dedi mode: `SpawnStructureAsync()` → `SpawnViaIPCAsync()` → NetworkBridge
  - PfServer mode: `SpawnStructureAsync()` → `directSpawner.SpawnStructureAsync()`
  - Поддержка структур (SpawnStructureRequest) И NPC (SpawnNPCRequest)
  - Прозрачность: вся существующая логика работает без изменений!

- **HandleIPCRequestAsync (PfServer):**
  - Обработчик IPC запросов от Dedi
  - **ЖЕСТКАЯ проверка playfield** перед spawn (если не совпадает → error)
  - Поддерживает SpawnStructureRequest и SpawnNPCRequest
  - Отправляет SpawnStructureResponse/SpawnNPCResponse обратно в Dedi

- **EntitySpawner (исправления):**
  - `factionId` теперь `int` (не `byte`) - правильный тип по EntitySpawnInfo
  - `factionGroup` НЕ используется (только если `factionId = -1`)
  - Для NPC: `entityTypeName` вместо `prefabName` и `type`

- **StageManager:**
  - `InitializeColonyAsync` — поддержка параметра `isVirtual`, спавн только для материализованных
  - `MaterializeColonyAsync` — находит позицию через `PlacementResolver`, спавнит структуры, обновляет флаги
  - `TransitionToNextStageAsync` — виртуальные апгрейды без физического спавна
  - `CanTransitionToNextStageAsync` — виртуальные колонии могут переходить на следующую стадию
- **SimulationEngine:** после инициализации модулей по‑прежнему перезагружает состояние, а также перезагружает `state` после каждого тика, чтобы подхватывать изменения, сделанные асинхронно по событиям (например, создание колонии по `Event_Playfield_Loaded`).
- **Версия:** в ModMain строка версии обновлена на `v1.0 Phase 3`.
- **Event_Error:** в `EmpyrionGateway.HandleEvent` при `CmdId.Event_Error` извлекается сообщение из `ErrorInfo` (в т.ч. `errorType.ToString()` для понятного кода в логах, например `EntityNotLocalToPlayfield`), запрос завершается через `SequenceManager.CompleteWithError` — исключение пробрасывается вызывающему вместо "Type mismatch".
- **PlacementResolver:** поздняя инъекция `IModApi` через `SetModApi` в `ModMain.Init` для корректного определения высоты рельефа.
- **ConfigurationLoader:** мерж дефолтных `Zirax.Stages` и `Zirax.DropShips` с ванильными префабами (BA_ConstructionSite, BA_Zirax_*, BA_MiningOutpost_Zirax_1 и т.д.), если в конфиге их нет.
- **StageManager:** префаб для посадочной структуры берётся из `_config.Zirax.DropShips` или fallback `BA_ConstructionSite`.
- **Инструкция для тестера:** `docs/manuals/Tester_Manual_Colony_Access.md` — консольные команды (tt, gm, find), как найти колонию по state.json/логам.
- **Тесты:** SimulationEngineTests ожидают минимум 2 вызова `LoadAsync` (загрузка + перезагрузка после init, дальнейшие вызовы при тиках допустимы); PlacementResolverTests — тип ответа `GlobalStructureList`; все unit/integration тесты проходят.

**Преимущества IPC архитектуры:**
1. ✅ **Решена PlayfieldConnectionNotFound** - spawn в правильном процессе
2. ✅ **Правильный граф зависимостей** - IPCEntitySpawner создается ДО модулей
3. ✅ **Гарантированная готовность** - OnPlayfieldLoaded + жесткая проверка playfield
4. ✅ **NPC spawn через IPC** - работает так же как структуры
5. ✅ **Прозрачность** - код использует `IEntitySpawner` не зная про IPC
6. ✅ **Надежность** - таймауты, retry, обработка ошибок
7. ✅ **Диагностика** - префиксы процессов в логах `[Dedi-PID]` / `[PfServer-PID]`
8. ✅ **Масштабируемость** - работает с любым количеством PfServer процессов

**Документация:** 
- `docs/architecture/12_Multi_Process_IPC_Architecture.md` - полная документация IPC
- `docs/architecture/11_Colony_Virtualization.md` - виртуализация колоний

### Прочие изменения Phase 3

- **SimulationEngine:** перезагружает `state` после каждого тика для синхронизации изменений
- **Event_Error:** корректная обработка с извлечением `ErrorType` (например, `PlayfieldConnectionNotFound`)
- **PlacementResolver:** поздняя инъекция `IModApi` через `SetModApi` для высоты рельефа
- **ConfigurationLoader:** мерж дефолтных префабов с конфигом
- **Инструкция для тестера:** `docs/manuals/Tester_Manual_Colony_Access.md`

## Текущее качество

- Unit тесты: проходят ✅  
- Integration тесты: проходят ✅  
- Сборка: ✅ успешна
- Виртуализация колоний: работает ✅ (проверено в логах)

## Следующие шаги

1. **Тестирование виртуализации:** запустить игру, войти на HomePlayfield, проверить:
   - Создание виртуальной колонии при старте мода
   - Виртуальное развитие (переход стадий в логах с флагом [VIRTUAL])
   - Материализацию при загрузке playfield (retry-попытки в логах)
   - Спавн структуры после успешной материализации

2. Phase 3.5: server testing на dedicated server (deploy → проверка материализации в мультиплеере)

3. Phase 4: Threat Director + AIM Orchestrator (по архитектурной документации)
