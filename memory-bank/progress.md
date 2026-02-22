# Progress — GalacticExpansion (GLEX)

## Текущий статус

**Дата обновления:** 22.02.2026  
**Phase 3.2 (Fix colony spawn):** ✅ РЕАЛИЗОВАНО  
**Phase 4 (Combat):** не начата

## Недавний прогресс

### Phase 3.2 — Исправление спавна базы колонии ✅

**Задача:** Исправить 3 критических бага, блокирующих спавн базы.

**Баг 1: Ресурсы уничтожались каждый тик**
- `SimulationEngine.OnSimulationTick()` перезагружал state из файла после каждого тика
- `resources before=0` на каждом из 120+ тиков при rate=100
- **Исправление:** убрана перезагрузка, заменена на `_state.IsDirty = true`

**Баг 2: Начальные ресурсы не выдавались**
- По FR-004 логистический корабль доставляет ресурсы
- Колония создавалась с `VirtualResources = 0`
- **Исправление:** при создании выдаётся 1100 ресурсов (ConstructionYard + BaseL1 + 10%)

**Баг 3: TryMaterializePendingColoniesAsync нигде не вызывался**
- Комментарии в коде противоречили друг другу
- **Исправление:** добавлен вызов в `ModMain.Game_Update()` секции Dedi

**Дополнительные улучшения:**
- Убраны все избыточные `_stateStore.LoadAsync()` из тиковых методов
- `ColonyManager` получил `SetSimulationState()` для работы с in-memory state
- StageManager: убран антипаттерн LoadAsync + ручное копирование полей
- Тесты обновлены: 175/175 проходят

**Изменённые файлы:**
- `SimulationEngine.cs` — убрана перезагрузка state из файла каждый тик
- `StageManager.cs` — начальные ресурсы + убраны LoadAsync/SaveAsync
- `ColonyManager.cs` — SetSimulationState + GetStateAsync + очистка
- `IColonyManager.cs` — добавлен SetSimulationState в интерфейс
- `ModMain.cs` — вызов TryMaterializePendingColoniesAsync + SetSimulationState
- `ColonyManagerTests.cs` — обновлён тест RemoveColony

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

### ✅ Phase 3.2 (Fix colony spawn) — НОВОЕ
- In-memory state как единственный источник правды
- Начальные ресурсы от логистического корабля (1100 ед.)
- TryMaterializePendingColoniesAsync вызывается из Game_Update
- Убраны все файловые операции из тикового цикла

## Что дальше

1. Тестирование на dedicated server: полный цикл LandingPending → BaseL1
2. Phase 3.5: server testing (мультиплеер, нагрузка)
3. Phase 4: Threat Director + AIM Orchestrator
