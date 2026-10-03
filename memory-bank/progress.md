# Progress — GalacticExpansion (GLEX)

## Текущий статус

**Дата обновления:** 03.10.2026  
**IPC-канал:** ✅ пакеты Dedi ↔ PfServer  
**DeserializeMessage:** ✅ поле `type`, затем конкретный класс  
**Материализация структуры:** ✅ `BA_ZiraxOutpost` в игре стоит на земле  
**Phase 3.5 (Fix spawn routing):** ✅  
**Phase 4 (Combat):** не начата

## Недавний прогресс

### Снос базы при смене стадии, 03.10.2026

`RemoveEntity` не убирает блоки `BA_ZiraxOutpost`. Dedicated теперь сначала вызывает `Request_Entity_Destroy`, затем IPC `RemoveEntity`. `DestroyEntityAsync` возвращает `bool`. Пока старый id на месте, новая стадия не спавнится и состояние колонии не меняется. В игре ещё не проверено.

### Высота рельефа, 02.10.2026 ✅

Раньше Dedi слал Y = 100 м (`DefaultTerrainHeight`) плюс отступ 10 м, база висела в воздухе. Теперь Y считается на PfServer: `GetTerrainHeightAt + 0.5`. Ошибка чтения рельефа отменяет спавн, запасные 100 м не подставляются. Проверено в игре: спавн у земли.

**Тесты:** unit-тесты интервала касания и пустого `Event_Ok` проходят. Ранее 175/175 после проверки сущности по IPC.

### Существование сущности, 02.10.2026 ✅

На Dedi `EntityExistsAsync` бросал `InvalidOperationException` каждую секунду и обрывал тик колонии. Проверка идёт IPC на PfServer (`IPlayfield.Entities`). Сбой IPC не бросает исключение. Снос базы — отдельно: `Request_Entity_Destroy`, затем `RemoveEntity`.

### Спавн префаба, 01.10.2026 ✅

`SpawnPrefab` возвращает `-1`, если имени нет в `Content/Prefabs` (без `.epb`). Дефолты `ConfigurationLoader` в игре отсутствуют. Рабочий конфиг: стадии `BA_ZiraxOutpost`, аванпосты `BA_ZiraxSkyminer`.

### IPC и Phase 3.5 ✅

`ModApi.Network` только из `EmpyrionModChannel`. На Dedi нет `EntitySpawner`: спавн через Gateway даёт `Event_Ok` без entity ID. PfServer: `NativePlayfieldSpawner`. Пустой `Event_Ok` на ожидающий SeqNr закрывает запрос.

## Что работает

### ✅ Phase 1–3.5
- Core Loop, Gateway (Dedi), State Store, Trackers
- Виртуальные колонии, экономика, стадии, `MaterializeColonyAsync`
- IPC: `PlayfieldReadyNotification`, `SpawnStructure` → `IPlayfield.SpawnPrefab` на высоте рельефа
- Дедлок `Game_Update` снят (`Task.Run`, throttle 3 с)

## Известная проблема

`colony.Position.Y` на Dedi может остаться запасной: для постановки на землю используются X и Z.

### Touch структур, 02.10.2026

Раз в минуту тик ждал `Request_Structure_Touch` и падал по таймауту, хотя игра отвечала `Event_Ok` без данных. Теперь пустой `Event_Ok` закрывает запрос, касание идёт раз в час и не держит тик. Id тот же, что у `SpawnPrefab`.
