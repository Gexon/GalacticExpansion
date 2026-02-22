# Active Context — GalacticExpansion (GLEX)

## Текущее состояние

**Дата обновления:** 22.02.2026  
**Фаза:** Phase 3.2 — Исправление спавна базы колонии ✅

## Главное за последние изменения

### Критическое исправление: In-memory State + начальные ресурсы + материализация

**Проблема:** Колония застревала в стадии ConstructionYard — база не спавнилась. 3 критических бага:

1. **Ресурсы уничтожались каждый тик** — `SimulationEngine` перезагружал state из файла (`_stateStore.LoadAsync()`) после каждого тика, уничтожая все in-memory изменения `VirtualResources`.
2. **Начальные ресурсы не выдавались** — логистический корабль по архитектуре FR-004 должен доставлять ресурсы, но `InitializeColonyAsync` создавал колонию с `VirtualResources = 0`.
3. **`TryMaterializePendingColoniesAsync` нигде не вызывался** — комментарии в коде противоречили друг другу.

**Решение — архитектурный принцип: In-memory state = единственный источник правды:**

1. **SimulationEngine.cs** — убрана перезагрузка state из файла каждый тик. Заменена на `_state.IsDirty = true`. Файл `state.json` пишется только при автосохранении (каждые 60 сек) и при shutdown.

2. **StageManager.InitializeColonyAsync** — при создании колонии выдаётся **1100 VirtualResources** (ConstructionYard.RequiredResources + BaseL1.RequiredResources + 10% запас). Хватает до BaseL1 без накопления. Самостоятельное накопление через ProductionRate — только с BaseL2.

3. **ModMain.Game_Update** — добавлен реальный вызов `TryMaterializePendingColoniesAsync()` в секции Dedi процесса.

4. **StageManager.TransitionToNextStageAsync** — убран антипаттерн: `LoadAsync()` + 20 строк ручного копирования полей. Теперь colony — объект из in-memory state, изменения применяются напрямую.

5. **StageManager.DowngradeColonyAsync** — аналогично убраны `LoadAsync/SaveAsync`.

6. **ColonyManager** — добавлен метод `SetSimulationState(SimulationState state)` для инъекции in-memory state. Все методы переведены на `GetStateAsync()` (in-memory state или fallback на файл при инициализации).

7. **IColonyManager** — добавлен метод `SetSimulationState` в интерфейс.

8. **Тест обновлён** — `RemoveColony_RemovesColonyFromState_Correctly` убрана проверка SaveAsync.

**Результат:** 175/175 тестов проходят, сборка без ошибок.

## Поток данных после исправления

```
Запуск:
  SimulationEngine.StartAsync() -> LoadAsync() -> _state (in-memory)
  -> SetSimulationState(_state) -> ColonyManager получает ссылку

Каждый тик (1 сек):
  OnSimulationTick() -> context.CurrentState = _state
    -> ColonyTickModule -> ColonyManager.UpdateColonyAsync(colony)
      -> EconomySimulator.UpdateProduction(colony) -> VirtualResources += produced [в памяти]
      -> StageManager.CanTransition? -> TransitionToNextStage [в памяти]
    -> _state.IsDirty = true

Game_Update (Dedi):
  -> TryMaterializePendingColoniesAsync() [in-memory state]

Каждые 60 сек:
  AutoSave -> _stateStore.SaveAsync(_state) -> state.json

Shutdown:
  StopAsync() -> SaveAsync(_state) -> state.json
```

## Следующие шаги

1. **Развёртывание и тестирование** на dedicated server:
   - Запуск → колония создаётся с 1100 ресурсов
   - LandingPending → ConstructionYard (мгновенно, RequiredResources=0)
   - ConstructionYard → BaseL1 (мгновенно, ресурсов достаточно)
   - BaseL1 → BaseL2 (через ~20 сек при ProductionRate=150)
   - Проверка материализации при входе игрока
2. Phase 3.5: server testing (мультиплеер)
3. Phase 4: Threat Director + AIM Orchestrator
