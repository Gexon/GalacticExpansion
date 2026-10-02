# Progress — GalacticExpansion (GLEX)

## Текущий статус

**Дата обновления:** 02.10.2026  
**IPC-канал:** ✅ пакеты Dedi ↔ PfServer  
**DeserializeMessage:** ✅ поле `type`, затем конкретный класс  
**Материализация структуры:** ✅ `BA_ZiraxOutpost` в игре стоит на земле  
**Phase 3.5 (Fix spawn routing):** ✅  
**Phase 4 (Combat):** не начата

## Недавний прогресс

### Высота рельефа, 02.10.2026 ✅

Раньше Dedi слал Y = 100 м (`DefaultTerrainHeight`) плюс отступ 10 м, база висела в воздухе. Теперь Y считается на PfServer: `GetTerrainHeightAt + 0.5`. Ошибка чтения рельефа отменяет спавн, запасные 100 м не подставляются. Проверено в игре: спавн у земли.

**Тесты:** unit 167/167 после этой правки.

### Спавн префаба, 01.10.2026 ✅

`SpawnPrefab` возвращает `-1`, если имени нет в `Content/Prefabs` (без `.epb`). Дефолты `ConfigurationLoader` в игре отсутствуют. Рабочий конфиг: стадии `BA_ZiraxOutpost`, аванпосты `BA_ZiraxSkyminer`.

### IPC и Phase 3.5 ✅

`ModApi.Network` только из `EmpyrionModChannel`. На Dedi нет `EntitySpawner`: спавн через Gateway даёт `Event_Ok` без entity ID. PfServer: `NativePlayfieldSpawner`. `TryCompleteResponse`: `data == null` → false.

## Что работает

### ✅ Phase 1–3.5
- Core Loop, Gateway (Dedi), State Store, Trackers
- Виртуальные колонии, экономика, стадии, `MaterializeColonyAsync`
- IPC: `PlayfieldReadyNotification`, `SpawnStructure` → `IPlayfield.SpawnPrefab` на высоте рельефа
- Дедлок `Game_Update` снят (`Task.Run`, throttle 3 с)

## Известная проблема

Нет открытой проблемы спавна. `colony.Position.Y` на Dedi может остаться запасной: для постановки на землю используются X и Z.
