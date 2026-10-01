# Progress — GalacticExpansion (GLEX)

## Текущий статус

**Дата обновления:** 01.10.2026  
**IPC-канал:** ✅ пакеты Dedi ↔ PfServer (логи v5–v7)  
**DeserializeMessage:** ✅ поле `type`, затем конкретный класс (unit 4/4). На dedicated ответ PfServer приходит  
**Материализация структуры:** ✅ в игре виден `BA_ZiraxOutpost`. Высота запасная, база висит над землёй  
**Phase 3.5 (Fix spawn routing):** ✅  
**Phase 4 (Combat):** не начата

## Недавний прогресс

### Спавн префаба, 01.10.2026 ✅ структура / ❌ высота

`SpawnPrefab` возвращает `-1`, если имени нет в `Content/Prefabs` (без `.epb`). Дефолты `ConfigurationLoader` (`BA_ConstructionSite`, `BA_Zirax_Small_1`, `BA_Zirax_Small_2`, `BA_Zirax_Medium_1`, `BA_Zirax_Large_1`, `BA_MiningOutpost_Zirax_*`) в игре отсутствуют. Подстановка срабатывает, когда `Zirax.Stages` пуст. В логе: `Zirax.Stages not in config or empty; using default`.

Рабочий конфиг: все стадии `BA_ZiraxOutpost`, аванпосты `BA_ZiraxSkyminer`. Проверено в игре: структура появляется. Координата Y = 110 (fallback 100 + отступ 10), XZ = 0, потому что поиск места принимает первую точку, а рельеф на Dedi не читается.

### IPC-канал, 01.10.2026 ✅

Вызовы `ModApi.Network` только из `EmpyrionModChannel` (`GalacticExpansion.dll`): ключ колбэка — `GetCallingAssembly().GetName().Name`. `DeserializeMessage` читает `type` через `JObject`.

### Phase 3.5 — EntitySpawner снят с Dedi ✅

Спавн через Gateway на Dedi даёт `Event_Ok` без entity ID. Dedi: `IPCEntitySpawner(NetworkBridge, IPlacementResolver)`. PfServer: `NativePlayfieldSpawner`. `TryCompleteResponse`: `data == null` → false.

**Тесты:** 175/175 на момент Phase 3.5. После канала INetwork и правки конфига тесты заново не гонялись.

## Что работает

### ✅ Phase 1–3.5
- Core Loop, Gateway (Dedi), State Store, Trackers
- Виртуальные колонии, экономика, стадии, `MaterializeColonyAsync`
- IPC: `PlayfieldReadyNotification`, `SpawnStructure` → `IPlayfield.SpawnPrefab`
- Дедлок `Game_Update` снят (`Task.Run`, throttle 3 с)

## Известная проблема

Структура спавнится над землёй. Высоту рельефа нужно снимать в PfServer перед `SpawnPrefab`.
