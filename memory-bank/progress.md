# Progress — GalacticExpansion (GLEX)

## Текущий статус

**Дата обновления:** 01.10.2026  
**IPC-канал (GetCallingAssembly):** ✅ пакеты ходят в обе стороны (логи v5, 18:30)  
**DeserializeMessage:** ✅ читает `type`, затем конкретный класс (unit 4/4). Прогон на dedicated ещё не сделан  
**Phase 3.5 (Fix spawn routing):** ✅ РЕАЛИЗОВАНО  
**Phase 4 (Combat):** не начата

## Недавний прогресс

### Phase 3.5 — Fix spawn routing: EntitySpawner выпилен из Dedi ✅

**Задача:** После Phase 3.4 материализация запускалась, но структуры не спавнились из-за некорректной маршрутизации спавна.

**Баг 1: IPCEntitySpawner.SpawnStructureAtTerrainAsync обходил IPC**
- `_directSpawner` (EntitySpawner) на Dedi отправлял `Request_Entity_Spawn` через Gateway на Dedi
- Empyrion отвечал `Event_Ok` (null data) вместо entity ID → timeout
- **Исправление:** EntitySpawner полностью удалён из Dedi. IPCEntitySpawner Dedi-конструктор: `(NetworkBridge, IPlacementResolver, mode, logger)`. Terrain height через PlacementResolver → SpawnStructureAsync (IPC).

**Баг 2: TryCompleteResponse NullReferenceException**
- `data.GetType()` на null (Event_Ok с data=null) бросал NRE → SeqNr не разрешался
- **Исправление:** `if (data == null) return false` в TryCompleteResponse

**Тесты:** 175/175 (158 unit + 17 integration) на момент Phase 3.5. После канала INetwork тесты заново не гонялись.

### IPC-канал, 01.10.2026 ✅ доставка / ❌ разбор JSON

`RegisterReceiver*` в игре ключует колбэк именем вызывающей сборки. Вызовы перенесены в `EmpyrionModChannel` (`GalacticExpansion.dll`). В логах v5 оба процесса получают `Received packet from 'GalacticExpansion'`.

`DeserializeMessage` читает `type` через `JObject` и десериализует конкретный класс. `DeserializeObject<IPCMessage>` убран: базовый класс абстрактный, в JSON первым идёт `pf`. Тесты: `NetworkBridgeDeserializeTests`. На dedicated этот билд ещё не запускали.

## Что работает

### ✅ Phase 1-2 (Foundation & Core Loop)
- Core Loop, Gateway (Dedi), State Store, Trackers

### ✅ Phase 3 (Domain)
- Spawning & Evolution: EntitySpawner (PfServer only!), StageManager
- Placement: PlacementResolver
- Economy: EconomySimulator, UnitEconomyManager
- Colony Management: ColonyManager, Colony Virtualization

### ✅ Phase 3.1-3.3 (Native Spawner, Colony Spawn, Materialization)
- NativePlayfieldSpawner, IPC PlayfieldReadyNotification, MaterializeColonyAsync

### ✅ Phase 3.4 (Fix deadlock + IPC reliability)
- Task.Run() вместо .GetAwaiter().GetResult() + throttle 3s
- Retry 3×500ms в SendNotificationToDedi

### ✅ Phase 3.5 (Fix spawn routing)
- EntitySpawner полностью выпилен из Dedi
- IPCEntitySpawner: Dedi = (NetworkBridge + IPlacementResolver), PfServer = (_directSpawner)
- TryCompleteResponse: null-check для Event_Ok
