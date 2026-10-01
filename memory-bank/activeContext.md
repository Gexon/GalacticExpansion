# Active Context — GalacticExpansion (GLEX)

## Текущее состояние

**Дата обновления:** 01.10.2026  
**Фаза:** IPC-канал доставляет пакеты в обе стороны. Следующий скоуп — `DeserializeMessage`.

## Последние изменения (01.10.2026) — канал INetwork

Пакеты Dedi ↔ PfServer доходят. Логи `logs-2006-10-01_v5`, 18:30: обе стороны пишут `Received packet from 'GalacticExpansion'`.

Игра (`Eleon.ModBridge.NetworkBridge` в `Assembly-CSharp.dll`) кладёт колбэк под `Assembly.GetCallingAssembly().GetName().Name`. Имя из yaml и папка мода не используются. `Send*(receiver)` ищет этот ключ через `TryGetValue`. Нет ключа — колбэк не вызывается, игра молчит, а `Send*` всё равно может вернуть `true`.

Вызов из `GalacticExpansion.Core.dll` регистрировал канал `GalacticExpansion.Core` при `receiver` `"GalacticExpansion"`. Исправление: единственная точка вызова — `EmpyrionModChannel` в сборке `GalacticExpansion.dll` (`NoInlining`, отдельный кадр стека). `NetworkBridge` ходит только через `IEmpyrionModChannel`.

`DeserializeMessage` в этот скоуп не входил. Пакет приходит и падает: `DeserializeObject<IPCMessage>` на абстрактном классе (`Path 'pf'`). Ответа нет, деди получает `IPC spawn timeout after 15s`. Чинить чтением поля `type` и десериализацией конкретного класса.

Phase 3.5 (EntitySpawner снят с Dedi) остаётся в силе, детали в `progress.md`. Поток материализации тот же: Dedi считает позицию, спавн только через IPC на PfServer.

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

1. Починить `DeserializeMessage`: не создавать `IPCMessage`, сначала прочитать `type`.
2. Повторить прогон материализации на dedicated: после разбора JSON должен быть ответ PfServer и EntityId.
3. Phase 4: Threat Director + AIM Orchestrator.
