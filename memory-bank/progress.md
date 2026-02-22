# Progress — GalacticExpansion (GLEX)

## Текущий статус

**Дата обновления:** 23.02.2026  
**Phase 3.4 (Fix deadlock + IPC):** ✅ РЕАЛИЗОВАНО  
**Phase 4 (Combat):** не начата

## Недавний прогресс

### Phase 3.4 — Исправление deadlock в Game_Update + IPC reliability ✅

**Задача:** Колония помечалась для материализации, но Game_Update зависал навсегда (deadlock) при первой попытке спавна. IPC PlayfieldReadyNotification не доходила до Dedi.

**Баг 1: Deadlock в Game_Update (КРИТИЧНЫЙ)**
- `.GetAwaiter().GetResult()` блокировал Unity main thread
- `await _gateway.SendRequestAsync<PlayfieldStats>()` внутри захватывала SynchronizationContext → deadlock
- **Исправление:** `Task.Run()` (fire-and-forget) + throttle 3s + `_materializationInProgress` guard

**Баг 2: IsPlayfieldReadyAsync — deadlock-prone проверка**
- `Request_Playfield_Stats` через Gateway → ответ не мог быть обработан при блокированном main thread
- **Исправление:** удалён метод `IsPlayfieldReadyAsync`. Готовность определяется через IPC PlayfieldReadyNotification

**Баг 3: PlayfieldReadyNotification не доходила до Dedi**
- Одна попытка отправки, timing issues
- **Исправление:** retry 3×500ms в `SendNotificationToDedi` + расширенная диагностика

**Изменённые файлы:**
- `ModMain.cs` — Task.Run + throttle + guard
- `ColonyManager.cs` — убран IsPlayfieldReadyAsync
- `IColonyManager.cs` — предупреждение о deadlock в комментариях
- `NetworkBridge.cs` — retry + диагностика
- `ColonyTickModule.cs` — обновлены комментарии

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

### ✅ Phase 3.3 (Fix materialization)
- IPC PlayfieldReadyNotification (PfServer → Dedi)
- MaterializeColonyAsync спавнит префаб текущей стадии
- Немедленный старт симуляции (без ожидания playfield)
- Guard `_simulationStarted` устраняет спам до старта

### ✅ Phase 3.4 (Fix deadlock + IPC reliability)
- Убран `.GetAwaiter().GetResult()` → Task.Run() с throttle 3s
- Убран `IsPlayfieldReadyAsync` (deadlock-prone)
- Retry 3×500ms в SendNotificationToDedi
- Fallback через Event_Playfield_Loaded (идемпотентно)

## Что дальше

1. Тестирование на dedicated server: Game_Update не зависает, материализация работает
2. Phase 3.5: server testing (мультиплеер, нагрузка)
3. Phase 4: Threat Director + AIM Orchestrator
