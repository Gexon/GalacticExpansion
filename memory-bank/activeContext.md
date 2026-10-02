# Active Context — GalacticExpansion (GLEX)

## Текущее состояние

**Дата обновления:** 02.10.2026  
**Фаза:** Материализация на dedicated подтверждена в игре. `BA_ZiraxOutpost` спавнится у земли.

## Последние изменения (02.10.2026) — высота рельефа

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

1. Phase 4: Threat Director + AIM Orchestrator.
