# Active Context — GalacticExpansion (GLEX)

## Текущее состояние

**Дата обновления:** 23.02.2026  
**Фаза:** Phase 3.5 — Fix spawn routing + выпилен EntitySpawner из Dedi ✅

## Последние изменения (Phase 3.5)

### Исправление маршрутизации спавна: EntitySpawner полностью удалён из Dedi

**Проблема:** После Phase 3.4 (deadlock fix) материализация запускалась, но структуры всё равно не спавнились.
Два связанных бага:

1. **IPCEntitySpawner.SpawnStructureAtTerrainAsync обходил IPC** — делегировал к `_directSpawner` (EntitySpawner), который отправлял `Request_Entity_Spawn` через ModAPI Gateway на Dedi. Empyrion отвечал `Event_Ok` (null data) вместо entity ID → NullReferenceException → timeout 10s.

2. **EmpyrionGateway.TryCompleteResponse: NullReferenceException** — `data.GetType()` на null (Event_Ok с data=null) → NRE → SeqNr не разрешался → timeout.

**Решение:**

1. **IPCEntitySpawner переработан:** На Dedi `_directSpawner` удалён полностью.
   - Dedi-конструктор: `(NetworkBridge, IPlacementResolver, ApplicationMode, ILogger)` — без IEntitySpawner
   - `SpawnStructureAtTerrainAsync` на Dedi: `_placementResolver.FindLocationAtTerrainAsync` → `SpawnStructureAsync` (IPC)
   - `DestroyEntityAsync`/`EntityExistsAsync` на Dedi: `InvalidOperationException`
   - PfServer-конструктор без изменений

2. **TryCompleteResponse: null-check** — `if (data == null) return false` перед `data.GetType()`

3. **ModMain: EntitySpawner убран** — `new EntitySpawner(...)` удалён из `InitializeGatewayAndModulesForDedi`

**Изменённые файлы:**
- `IPCEntitySpawner.cs` — полная переработка (Dedi без _directSpawner, IPlacementResolver)
- `EmpyrionGateway.cs` — null-check в TryCompleteResponse
- `ModMain.cs` — убран EntitySpawner из Dedi, новый конструктор IPCEntitySpawner

**Результат:** 175/175 тестов (158 unit + 17 integration), сборка без ошибок.

## Поток данных

```
Инициализация Dedi:
  InitializeGatewayAndModulesForDedi() → регистрация модулей
  → IPCEntitySpawner(networkBridge, placementResolver, Dedi, logger) — БЕЗ EntitySpawner!
  → Task.Run: SimulationEngine.StartAsync() → _simulationStarted = true
  → SetSimulationState(_state) → ColonyManager получает ссылку

Материализация (Dedi → IPC → PfServer):
  StageManager.MaterializeColonyAsync
    → IPCEntitySpawner.SpawnStructureAtTerrainAsync
      → _placementResolver.FindLocationAtTerrainAsync (terrain height на Dedi)
      → IPCEntitySpawner.SpawnStructureAsync → SpawnViaIPCAsync
        → NetworkBridge → PfServer: NativePlayfieldSpawner выполняет спавн
```

## Следующие шаги

1. **Развёртывание и тестирование** на dedicated server
2. Phase 4: Threat Director + AIM Orchestrator
