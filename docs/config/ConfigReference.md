# Config Reference (GLEX)

**Назначение**: справочник по конфигурации мода: лимиты, интервалы, профили, включение механик.  
**Аудитория**: разработчики.  
**Статус**: draft (ключи зафиксированы по черновику; уточним при реализации кода).  
**Связанные документы**: [`state_schema.md`](state_schema.md), [`../architecture/06_security_model.md`](../architecture/06_security_model.md).  

## Где лежат конфиги

GLEX будет придерживаться распространённого паттерна из `docs/examples`:

- рядом с DLL лежит `ModAppConfig.json` (указатель “где основной конфиг”),
- основной конфиг/данные — в `Saves/Games/<SaveGame>/Mods/GLEX/...` и создаются при первом старте (если отсутствуют).

## Базовые ключи (MVP)

### Инициализация

- `HomePlayfield` (string): имя “материнской” планеты/плейфилда.

### Лимиты и квоты (FR-003)

- `MaxColoniesPerPlayfield` (int, MVP=1)
- `MaxActiveAIVessels` (int, на playfield)
- `MaxGuardsNearColony` (int)
- `MaxBuildersNearColony` (int)
- `MaxResourceOutposts` (int)
- `MaxDroneWavesPerHour` (int)
- `MaxAIMCommandsPerMinute` (int) — защита от спама консольных команд

### “Логистический корабль” (FR-004)

- `DropShipPrefabName` (string) — например `GLEX_DropShip_T1`
- `DropShipSpawnAltitude` (float/int)
- `FlightMode` (string enum): `RealAI` | `CinematicFallback`
- `FlightDurationSeconds` (int)

### Стадийность базы (FR-005)

- `Stages` (array): список стадий с привязкой к prefab’ам и условиям
  - `StageId` (string)
  - `PrefabName` (string)
  - `MinTimeSeconds` (int)
  - `RequiredResources` (int)

### Угрозы / AIM (FR-006)

- `EnableThreats` (bool)
- `ThreatTickSeconds` (int)
- `AllowedAimCommands` (array of strings/regex) — whitelist (см. `06_security_model.md`)

### Ресурсы и аутпосты (FR-007)

- `EnableResourceOutposts` (bool)
- `ResourceProductionRate` (float)
- `OutpostPrefabs` (map/array): например `GLEX_MinerOutpost_L1` и т.п.

### Экспансия (FR-008)

- `EnableExpansion` (bool)
- `ExpansionDelaySeconds` (int)
- `ExpansionCandidatePlayfields` (array of string) — если хотим явный список в MVP

## Пример (минимальный, черновой)

См. `docs/config/config_examples/minimal.json`.

> Важно: формат (JSON/YAML) закрепим в коде. Здесь ключи описаны логически и могут быть перенесены 1-в-1 в JSON.
