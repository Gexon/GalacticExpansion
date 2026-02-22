# Active Context — GalacticExpansion (GLEX)

## Текущее состояние

**Дата обновления:** 23.02.2026  
**Фаза:** Phase 3.3 — Исправление материализации колоний ✅

## Последние изменения (Phase 3.3)

### Исправление материализации: IPC PlayfieldReadyNotification + немедленный старт симуляции

**Проблема:** После Phase 3.2 колония создавалась, ресурсы накапливались, но структуры не спавнились на playfield. 3 бага + 1 архитектурное улучшение:

1. **PendingMaterialization никогда не становилась true** — `ColonyTickModule` подписывался на `GameEventReceived` в `InitializeAsync`, но это происходило через 30 сек после `Event_Playfield_Loaded` — событие пропускалось.
2. **MaterializeColonyAsync спавнила хардкод** — использовался `DropShips.PrefabName` вместо префаба текущей стадии колонии.
3. **Спам "State file not found" (1433 записи)** — `Game_Update` вызывал `TryMaterializePendingColoniesAsync` до старта симуляции, когда `_simulationState == null`.
4. **Симуляция зависела от игроков** — запускалась только при `Event_Playfield_Loaded` (когда кто-то заходил на планету).

**Решение:**

1. **IPC PlayfieldReadyNotification (PfServer → Dedi)**  
   PfServer отправляет уведомление после `OnPlayfieldLoaded` (когда NativePlayfieldSpawner и IPC handler готовы). Dedi получает и вызывает `EnsurePlayfieldColoniesSpawnedAsync(playfield)` → `PendingMaterialization = true`.

2. **MaterializeColonyAsync** теперь спавнит префаб текущей стадии колонии (`_config.Zirax.Stages` → `PrefabName`), а не хардкод из DropShips.

3. **Guard `_simulationStarted`** в `Game_Update` — `TryMaterializePendingColoniesAsync` вызывается только после старта симуляции.

4. **Немедленный старт симуляции** — перенесён в конец `InitializeGatewayAndModulesForDedi`. Колонии живут автономно с первой секунды, не зависят от подключения игроков.

**Изменённые файлы:**
- `ModMain.cs` — немедленный старт симуляции, guard `_simulationStarted`, подписка на `OnPlayfieldReadyReceived`, PfServer отправляет `PlayfieldReadyNotification`
- `IPCProtocol.cs` — класс `PlayfieldReadyNotification` (fire-and-forget IPC)
- `NetworkBridge.cs` — `SendNotificationToDedi()`, `OnPlayfieldReadyReceived` событие, десериализация нового типа
- `StageManager.cs` — `MaterializeColonyAsync` спавнит префаб текущей стадии

**Результат:** 175/175 тестов (158 unit + 17 integration), сборка без ошибок.

## Поток данных

```
Инициализация Dedi:
  InitializeGatewayAndModulesForDedi() → регистрация модулей
  → Task.Run: SimulationEngine.StartAsync() → _simulationStarted = true
  → SetSimulationState(_state) → ColonyManager получает ссылку
  → колония живёт автономно с первой секунды

Каждый тик (1 сек):
  OnSimulationTick() → context.CurrentState = _state
    → ColonyTickModule → ColonyManager.UpdateColonyAsync(colony)
      → EconomySimulator.UpdateProduction → VirtualResources += produced
      → StageManager.CanTransition? → TransitionToNextStage
    → _state.IsDirty = true

Материализация (PfServer → Dedi):
  PfServer: OnPlayfieldLoaded → NativePlayfieldSpawner готов
    → NetworkBridge.SendNotificationToDedi(PlayfieldReadyNotification)
  Dedi: OnPlayfieldReadyReceived → EnsurePlayfieldColoniesSpawnedAsync
    → PendingMaterialization = true
  Game_Update → TryMaterializePendingColoniesAsync → IPC → PfServer спавнит

Автосохранение (60 сек): SaveAsync(_state) → state.json
Shutdown: StopAsync() → SaveAsync(_state) → state.json
```

## Следующие шаги

1. **Развёртывание и тестирование** на dedicated server:
   - Запуск → симуляция стартует сразу → колония создаётся с 1100 ресурсов
   - LandingPending → ConstructionYard (мгновенно)
   - Через 10 мин (MinTimeSeconds=600) → BaseL1
   - Игрок заходит → PfServer отправляет PlayfieldReadyNotification → материализация
   - Проверить: видна ли структура ConstructionYard/BaseL1 на playfield
2. Phase 3.5: server testing (мультиплеер)
3. Phase 4: Threat Director + AIM Orchestrator
