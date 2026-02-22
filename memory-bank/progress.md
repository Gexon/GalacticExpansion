# Progress — GalacticExpansion (GLEX)

## Текущий статус

**Дата обновления:** 23.02.2026  
**Phase 3.3 (Fix materialization):** ✅ РЕАЛИЗОВАНО  
**Phase 4 (Combat):** не начата

## Недавний прогресс

### Phase 3.3 — Исправление материализации колоний ✅

**Задача:** Колония развивалась виртуально, но структуры не спавнились на playfield.

**Баг 1: PendingMaterialization никогда не становилась true**
- `ColonyTickModule` подписывался на `GameEventReceived` после `Event_Playfield_Loaded` (опоздание 30 сек)
- **Исправление:** PfServer отправляет IPC `PlayfieldReadyNotification` → Dedi вызывает `EnsurePlayfieldColoniesSpawnedAsync`

**Баг 2: MaterializeColonyAsync спавнила хардкод**
- Использовался `DropShips.PrefabName` вместо префаба текущей стадии
- **Исправление:** спавн из `_config.Zirax.Stages` по `colony.Stage`

**Баг 3: Спам "State file not found" (1433 записи)**
- `Game_Update` вызывал `TryMaterializePendingColoniesAsync` до старта симуляции
- **Исправление:** guard `_simulationStarted` в `Game_Update`

**Архитектурное улучшение: немедленный старт симуляции**
- Симуляция запускается сразу в конце `InitializeGatewayAndModulesForDedi`
- Колонии развиваются автономно, не зависят от подключения игроков
- Убрана зависимость от `Event_Playfield_Loaded` для старта

**Изменённые файлы:**
- `ModMain.cs` — немедленный старт, guard, IPC подписка, PfServer отправка уведомления
- `IPCProtocol.cs` — класс `PlayfieldReadyNotification`
- `NetworkBridge.cs` — `SendNotificationToDedi`, событие `OnPlayfieldReadyReceived`, десериализация
- `StageManager.cs` — `MaterializeColonyAsync` спавнит текущую стадию

**Тесты:** 175/175 (158 unit + 17 integration)

## Что работает

### ✅ Phase 1-2 (Foundation & Core Loop)
- Core Loop, Gateway (Dedi), State Store, Trackers

### ✅ Phase 3 (Domain)
- Spawning & Evolution: EntitySpawner, StageManager
- Placement: PlacementResolver
- Economy: EconomySimulator, UnitEconomyManager
- Colony Management: ColonyManager, Colony Virtualization

### ✅ Phase 3.1 (Native Spawner)
- NativePlayfieldSpawner, UnityTypeConverter
- Упрощенная инициализация PfServer без Gateway

### ✅ Phase 3.2 (Fix colony spawn)
- In-memory state как единственный источник правды
- Начальные ресурсы от логистического корабля (1100 ед.)
- TryMaterializePendingColoniesAsync вызывается из Game_Update

### ✅ Phase 3.3 (Fix materialization)
- IPC PlayfieldReadyNotification (PfServer → Dedi)
- MaterializeColonyAsync спавнит префаб текущей стадии
- Немедленный старт симуляции (без ожидания playfield)
- Guard `_simulationStarted` устраняет спам до старта

## Что дальше

1. Тестирование на dedicated server: полный цикл → ConstructionYard видим → BaseL1 через 10 мин
2. Phase 3.5: server testing (мультиплеер, нагрузка)
3. Phase 4: Threat Director + AIM Orchestrator
