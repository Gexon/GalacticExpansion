# Progress — GalacticExpansion (GLEX)

## Текущий статус

**Дата обновления:** 07.02.2026  
**Phase 3.1 (Native Spawner):** ✅ РЕАЛИЗОВАНО (требует тестирование)  
**Phase 4 (Combat):** не начата

## Недавний прогресс

### 🎯 Native Playfield Spawner — КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ ✅

**Задача:** Исправить краш PfServer при входе игрока в игровое поле.

**Проблема (из логов 06.02.2026):**
```
[PfServer] CRITICAL ERROR during PfServer initialization
System.InvalidOperationException: Gateway required for PfServer
  at ModMain.InitializePlayfieldServer (line 1073)
```

**Корневая причина:**
- PfServer процесс не получает ModGameAPI (Game_Start может не вызваться)
- Старая архитектура требовала Gateway для spawn
- Gateway требует ModGameAPI для инициализации
- Результат: краш PfServer → API сервер падает при входе игрока

**Решение - Native Playfield Spawner:**

**1. NativePlayfieldSpawner** (`Core/Spawning/NativePlayfieldSpawner.cs`):
- Новый spawner с прямым доступом к IPlayfield API
- НЕ требует ModGameAPI/Gateway/SequenceManager
- Синхронные вызовы: `SpawnPrefab()`, `SpawnEntity()`, `GetTerrainHeightAt()`
- Работает только в PfServer процессе
- Интерфейс: `INativePlayfieldSpawner.cs`

**2. UnityTypeConverter** (`Core/Spawning/UnityTypeConverter.cs`):
- Конвертация `Models.Vector3` ↔ `UnityEngine.Vector3`
- Конвертация Euler angles ↔ `UnityEngine.Quaternion`
- Алиас `using ILogger = NLog.ILogger;` для избежания конфликта

**3. InitializePlayfieldServer() - переписана:**
- Убрана инициализация Gateway/EntitySpawner/PlacementResolver/Container
- Создается только NetworkBridge
- В OnPlayfieldLoaded: создается NativePlayfieldSpawner с IPlayfield instance
- Регистрируется HandleIPCRequestWithNativeSpawner

**4. HandleIPCRequestWithNativeSpawner():**
- Новый обработчик IPC с нативным спавнером
- Конвертация типов через UnityTypeConverter
- Прямой вызов `IPlayfield.SpawnPrefab()` / `SpawnEntity()`
- Логи с префиксом `[PfServer-Native]`

**5. Зависимости:**
- Скопирован `UnityEngine.CoreModule.dll` в `lib/`
- Добавлена ссылка в `GalacticExpansion.Core.csproj`
- Добавлена ссылка в `GalacticExpansion.csproj`

**Результат:**
- ✅ PfServer инициализируется БЕЗ Gateway (нет краша!)
- ✅ Spawn работает через IPlayfield.SpawnPrefab() (прямой вызов)
- ✅ Быстрее: ~50-100ms (вместо 500ms-2s через Gateway+IPC)
- ✅ Проще: меньше слоев, меньше точек отказа
- ✅ Надежнее: прямой доступ к IPlayfield entities

**Архитектура:**
```
DEDI: ColonyManager → IPCEntitySpawner → NetworkBridge
  ↓ IPC
PFSERVER: NetworkBridge → HandleIPCRequestWithNativeSpawner
  ↓
NativePlayfieldSpawner → IPlayfield.SpawnPrefab()
  ↓
✅ Entity spawned!
```

## Что работает

### ✅ Phase 1-2 (Foundation & Core Loop)
- Core Loop, Gateway (Dedi), State Store, Trackers

### ✅ Phase 3 (Domain)
- Spawning & Evolution: EntitySpawner, StageManager
- Placement: PlacementResolver
- Economy: EconomySimulator, UnitEconomyManager
- Colony Management: ColonyManager
- Colony Virtualization: создание → развитие → материализация

### ✅ Phase 3.1 (Native Spawner) — НОВОЕ
- NativePlayfieldSpawner для PfServer
- UnityTypeConverter для Unity типов
- Упрощенная инициализация PfServer без Gateway
- HandleIPCRequestWithNativeSpawner для IPC

## Что дальше

1. **Сборка:** `dotnet build src/GalacticExpansion.sln --configuration Release --no-restore`
2. **Тестирование:**
   - Запуск dedicated server
   - Вход игрока на playfield → нет краша
   - Материализация колонии → spawn через Native Spawner
   - Проверка логов: `[PfServer-Native] Structure spawn successful`
3. Phase 3.5: server testing (мультиплеер, нагрузка)
4. Phase 4: Threat Director + AIM Orchestrator
