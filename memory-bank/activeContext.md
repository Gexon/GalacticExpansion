# Active Context — GalacticExpansion (GLEX)

## Текущее состояние

**Дата обновления:** 01.10.2026  
**Фаза:** Материализация на dedicated подтверждена в игре. `BA_ZiraxOutpost` спавнится. Структура висит над землёй: высота на Dedi — запасные 100 м плюс отступ 10 м.

## Последние изменения (01.10.2026) — спавн префаба

Логи `logs-2006-10-01_v7`: IPC доходит, `SpawnPrefab("BA_Zirax_Medium_1")` возвращает `EntityId=-1`. Такого файла в `Content/Prefabs` нет. Имя бралось не из правки пользователя, а из дефолта `ConfigurationLoader`, потому что в JSON не было `Zirax.Stages`.

`Configuration` сериализуется с `MemberSerialization.OptIn`. Секции `Prefabs`, `ColonyEvolution`, `Economy`, `ThreatDirector`, `Hostility`, `Advanced` загрузчик отбрасывает. Ключи `LandingPad` / `MediumBase` не совпадают с `ColonyStage` (`ConstructionYard`, `BaseL1`…`BaseMax`).

В `config/Configuration.json` добавлен `Zirax` со стадиями на `BA_ZiraxOutpost` и аванпостами `BA_ZiraxSkyminer`. Блок `Prefabs` удалён. Тот же файл скопирован в `Content/Mods/GalacticExpansion/`. Игрок видит заспавненный `BA_ZiraxOutpost`.

Высота: `OnPlayfieldLoaded` и `IPlayfield.GetTerrainHeightAt` есть только в PfServer. Кэш `PlacementResolver` на Dedi пуст, `FindLocationAtTerrainAsync` ставит Y = 100. `MaterializeColonyAsync` добавляет `heightOffset` 10. В логе точка `(0, 110, 0)`.

Канал INetwork и разбор JSON по полю `type` остаются как в `systemPatterns.md`.

## Поток данных

```
Dedi: MaterializeColonyAsync
  → префаб из Zirax.Stages, где Stage == colony.Stage.ToString()
  → PlacementResolver: Y = 100, если playfield не в кэше
  → IPC SpawnStructure → PfServer NativePlayfieldSpawner.SpawnPrefab
```

## Следующие шаги

1. Брать высоту рельефа на PfServer (`GetTerrainHeightAt`) и ставить структуру на землю.
2. Phase 4: Threat Director + AIM Orchestrator.
