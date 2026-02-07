# System Patterns — GalacticExpansion (GLEX)

## Архитектурные паттерны

### Модульный монолит
- Единая DLL с четкими границами модулей.
- Расширяемость через интерфейсы и события.

### Event-Driven Architecture
- Взаимодействие через `EventBus`.
- Минимизация прямых зависимостей между модулями.

### Repository Pattern
- `IStateStore` управляет загрузкой/сохранением state.json.
- Атомарная запись и бэкапы — обязательны.

## Ключевые технические решения

### Native Playfield Spawner Pattern (Phase 3.1 — НОВОЕ!)

**Проблема:** PfServer крашится при инициализации из-за отсутствия ModGameAPI.

**Решение:** Прямой спавн через IPlayfield API без Gateway/ModGameAPI.

```csharp
// НОВАЯ АРХИТЕКТУРА PfServer (без Gateway):
OnPlayfieldLoaded → IPlayfield instance
    ↓
NativePlayfieldSpawner(pfInstance)
    ↓
IPlayfield.SpawnPrefab() / SpawnEntity() (СИНХРОННЫЙ!)
```

**Ключевые компоненты:**

1. **NativePlayfieldSpawner:**
   - Принимает `IPlayfield` напрямую (не через wrapper)
   - Синхронные вызовы (нет async overhead)
   - Не требует Gateway/SequenceManager/ModGameAPI
   - `SpawnStructureAsync()` → `IPlayfield.SpawnPrefab(prefabName, position)`
   - `SpawnNPCAsync()` → `IPlayfield.SpawnEntity(entityType, position, rotation)`
   - `GetTerrainHeight()` → `IPlayfield.GetTerrainHeightAt(x, z)`

2. **UnityTypeConverter:**
   - Конвертация между `Models.Vector3` и `UnityEngine.Vector3`
   - Конвертация Euler angles в `UnityEngine.Quaternion`
   - **ВАЖНО:** `using ILogger = NLog.ILogger;` (избежать конфликта с UnityEngine.ILogger)

3. **InitializePlayfieldServer() - упрощенная:**
   - ❌ НЕ создает Gateway
   - ❌ НЕ создает EntitySpawner/PlacementResolver/Container
   - ❌ НЕ извлекает ModGameAPI
   - ✅ Только NetworkBridge + NativePlayfieldSpawner в OnPlayfieldLoaded

**Преимущества:**
- ✅ Нет краша при отсутствии ModGameAPI
- ✅ Быстрее (~50-100ms vs 500ms-2s)
- ✅ Проще (меньше слоев)
- ✅ Надежнее (прямой доступ к IPlayfield)

### State Sync Pattern
- Изменения колонии в критичных методах делаются через объект из `StateStore.LoadAsync`.
- После обновления — обязательное `SaveAsync`.

### Interface Contract Alignment
- Реализации обязаны повторять порядок параметров и смысл контрактов интерфейсов.
- Тесты проверяют соответствие контрактам.

### Event_Error Handling (ModAPI)
- При ответе игры на запрос с ошибкой приходит `CmdId.Event_Error` и объект `ErrorInfo` (поле `errorType` — enum `ErrorType`).
- Шлюз в `HandleEvent` обрабатывает Event_Error: извлекает текст из `ErrorInfo`, вызывает `SequenceManager.CompleteWithError(seqNr, exception)`.

### Playfield Load Handling & Colony Virtualization (Phase 3)

**Виртуализация колоний** — ключевой паттерн для надёжного создания и развития колоний:

1. **Создание виртуальной колонии** (при старте мода):
   - Не зависит от событий — создаётся сразу в `ColonyTickModule.InitializeAsync`
   - `IsVirtual = true`, без физических структур
   - Развивается в БД: ресурсы, юниты, переходы стадий

2. **Материализация** (через IPC из Dedi в PfServer):
   - Событие `Event_Playfield_Loaded` → пометка `PendingMaterialization = true`
   - Dedi: `ColonyManager.TryMaterializePendingColoniesAsync()` → IPC команда
   - PfServer: `HandleIPCRequestWithNativeSpawner()` → `NativePlayfieldSpawner.SpawnStructureAsync()`
   - При успехе: `IsVirtual = false`, EntityId сохраняется

**Данные события:** `PlayfieldLoad` (Mif/Eleon.Modding) — поля `sec`, `playfield`, `processId`

## Важные инварианты

1. `State.json` всегда валиден (атомарная запись + бэкапы).
2. `SeqNr` уникальны и корректно сопоставляются.
3. Rate limiting обязателен для ModAPI запросов (только Dedi).
4. Спавн в Dedi через `IEntitySpawner` (IPCEntitySpawner).
5. Спавн в PfServer через `NativePlayfieldSpawner` (прямой IPlayfield API).
6. **Виртуальные колонии:** физические операции запрещены; только обновление в БД.
7. **Multi-process:** Spawn работает только из PfServer процесса.
8. **UnityEngine.ILogger:** При using UnityEngine добавлять `using ILogger = NLog.ILogger;`
