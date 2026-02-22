# Active Context — GalacticExpansion (GLEX)

## Текущее состояние

**Дата обновления:** 23.02.2026  
**Фаза:** Phase 3.4 — Fix deadlock + IPC reliability ✅

## Последние изменения (Phase 3.4)

### Исправление deadlock в Game_Update и ненадёжности IPC

**Проблема:** После Phase 3.3 колония создавалась, ресурсы накапливались, `PendingMaterialization` помечалась — но `TryMaterializePendingColoniesAsync` зависала навсегда (deadlock). Структуры не спавнились.

**Корневая причина (deadlock):**
```csharp
// ModMain.Game_Update() — БЫЛО (вызывало deadlock):
_colonyManager.TryMaterializePendingColoniesAsync().GetAwaiter().GetResult();
```
`.GetAwaiter().GetResult()` блокировал Unity main thread. Внутри `TryMaterializePendingColoniesAsync` вызывался `await _gateway.SendRequestAsync<PlayfieldStats>(...)` — continuation от `await` захватывала `SynchronizationContext` (Unity) и пыталась вернуться на заблокированный main thread → **deadlock навсегда**.

**Доказательства из лога:**
- `attempt 1/30` — единственная попытка за 170 секунд работы
- `Request_Playfield_Stats timed out after 2000ms` — ответ не мог быть обработан
- Нет `attempt 2/30`, `not ready yet`, `Giving up` — Game_Update мёртв
- Тики симуляции продолжались (они в Task.Run, не на main thread)

**Вторичная проблема:** IPC `PlayfieldReadyNotification` от PfServer не доходила до Dedi (однократная отправка без retry).

**Решение:**

1. **Убран `.GetAwaiter().GetResult()`** — заменён на `Task.Run()` (fire-and-forget на ThreadPool).
   Добавлен throttle (3 сек) и `_materializationInProgress` guard от параллельных вызовов.

2. **Убран `IsPlayfieldReadyAsync` + `Request_Playfield_Stats`** — deadlock-prone проверка готовности.
   Готовность playfield определяется через IPC `PlayfieldReadyNotification` от PfServer (гарантированно после инициализации NativePlayfieldSpawner).

3. **Retry в `SendNotificationToDedi`** — 3 попытки с задержкой 500ms + расширенная диагностика.

4. **Fallback через `Event_Playfield_Loaded`** — если IPC не дойдёт, `ColonyTickModule.OnGameEvent` всё ещё вызывает `EnsurePlayfieldColoniesSpawnedAsync` (идемпотентно).

**Изменённые файлы:**
- `ModMain.cs` — Task.Run + throttle + _materializationInProgress guard
- `ColonyManager.cs` — убран IsPlayfieldReadyAsync, TryMaterialize без блокирующих вызовов gateway
- `IColonyManager.cs` — обновлены комментарии (предупреждение о deadlock)
- `NetworkBridge.cs` — retry (3×500ms) в SendNotificationToDedi + диагностика
- `ColonyTickModule.cs` — обновлены комментарии (fallback роль Event_Playfield_Loaded)

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
    → NetworkBridge.SendNotificationToDedi(PlayfieldReadyNotification) [retry 3×500ms]
  Dedi: OnPlayfieldReadyReceived → EnsurePlayfieldColoniesSpawnedAsync
    → PendingMaterialization = true
  Fallback: Event_Playfield_Loaded → ColonyTickModule.OnGameEvent → EnsurePlayfield (идемпотентно)
  Game_Update → Task.Run(TryMaterializePendingColoniesAsync) [throttle 3s] → IPC → PfServer спавнит

Автосохранение (60 сек): SaveAsync(_state) → state.json
Shutdown: StopAsync() → SaveAsync(_state) → state.json
```

## Следующие шаги

1. **Развёртывание и тестирование** на dedicated server:
   - Проверить: Game_Update не зависает, тики продолжаются
   - Проверить: PlayfieldReadyNotification доходит (retry)
   - Проверить: материализация колонии → ConstructionYard видим на playfield
2. Phase 3.5: server testing (мультиплеер)
3. Phase 4: Threat Director + AIM Orchestrator
