# Active Context — GalacticExpansion (GLEX)

## Текущее состояние

**Дата обновления:** 03.10.2026  
**Фаза:** Смена стадии должна сносить старую базу до спавна новой. В игре ещё не проверено.

## Последние изменения (03.10.2026) — снос базы при апгрейде

`BA_ZiraxOutpost` — структура. `IPlayfield.RemoveEntity` убирает запись сущности, блоки базы остаются, и новый префаб встаёт в ту же точку. На dedicated `DestroyEntityAsync` сначала шлёт `Request_Entity_Destroy` (тот же id, что у touch), затем IPC `RemoveEntity`, затем `EntityExists`. Метод возвращает `bool`. Если id ещё есть, `TransitionToNextStageAsync` выходит до спавна, смены стадии и списания ресурсов. Тик колонии при этом не пишет `Error updating colony`.

## Предыдущее (02.10.2026) — touch структур

Касание структуры с dedicated остаётся `Request_Structure_Touch` по тому же `MainStructureId`, что вернул `SpawnPrefab`. Игра отвечает `Event_Ok` без тела примерно за 50 мс. Раньше Gateway этот ответ не закрывал, и тик колонии ждал таймаут 3 секунды. Теперь пустой `Event_Ok` завершает запрос. Интервал — раз в час (`Colony.LastMaintenanceTime`), тик ответ не ждёт. Приоритет команды — низкий.

## Предыдущее (02.10.2026) — проверка сущности по IPC

Тик материализованной колонии на Dedi не вызывает Gateway, чтобы узнать, жива ли главная структура. `EntityExistsAsync` шлёт `EntityExists`. PfServer отвечает по `IPlayfield.Entities`. Нет ответа, обрыв или `ok=false` дают `false`, без `Error updating colony`.

## Предыдущее (02.10.2026) — высота рельефа

Игрок подтвердил: структура стоит на земле. Высоту считает PfServer, не dedicated.

`SpawnStructureAtTerrainAsync` на Dedi шлёт X/Z, `SnapToTerrain=true` (`snap`) и `HeightOffset` (`hoff`, у материализации и посадочной структуры 0.5 м). `HandleIPCRequestWithNativeSpawner` перед `SpawnPrefab`: `Y = GetTerrainHeightAt(x, z) + offset`. Если рельеф прочитать нельзя, спавн отменяется. Абсолютный `SpawnStructureAsync` флаг не ставит. `colony.Position` на Dedi по-прежнему может хранить запасную Y: повторный спавн берёт только X и Z.

Префаб стадии, канал INetwork и разбор JSON по полю `type` остаются как в `systemPatterns.md`.

## Поток данных

```
Dedi: MaterializeColonyAsync
  → префаб из Zirax.Stages, где Stage == colony.Stage.ToString()
  → IPC SpawnStructure (snap=true, X/Z, hoff=0.5)
PfServer: GetTerrainHeightAt → Y → NativePlayfieldSpawner.SpawnPrefab
```

## Следующие шаги

1. В игре: после смены стадии на планете одна база. Если `Request_Entity_Destroy` ответит `Event_Ok`, а старая база останется — следующий шаг тот же запрос из процесса playfield.
2. Phase 4: Threat Director + AIM Orchestrator.
