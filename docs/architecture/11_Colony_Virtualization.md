# Виртуализация колоний (Colony Virtualization)

## Обзор

**Виртуализация колоний** — это механизм, позволяющий колониям существовать и развиваться в базе данных (state) без физического спавна структур и юнитов в игровом мире. Виртуальные колонии **материализуются** (спавнятся в мир) только когда игрок входит на соответствующий playfield.

### Зачем нужна виртуализация?

1. **Решение проблемы загрузки playfield**: Раньше колония создавалась при событии `Event_Playfield_Loaded`, но это приводило к ошибкам, если playfield еще не полностью готов к операциям ModAPI.

2. **Независимость от игрока**: Колония начинает развиваться сразу при старте игры, независимо от того, посетил ли игрок playfield.

3. **Производительность**: Виртуальные колонии не нагружают игровой мир структурами и NPC до тех пор, пока игрок не придет на playfield.

4. **Консистентность**: Экономика, ресурсы и юниты развиваются непрерывно, даже когда playfield выгружен из памяти.

---

## Жизненный цикл виртуальной колонии

### 1. Создание виртуальной колонии

Виртуальная колония создается при инициализации мода (`ColonyTickModule.InitializeAsync`), если:
- `state.Colonies.Count == 0` (нет колоний в базе данных)
- `_config.EnableExpansion == true` (экспансия включена)

```csharp
// ColonyTickModule.cs
await CreateInitialVirtualColonyAsync();
```

**Параметры виртуальной колонии:**
- `Playfield`: Название homePlayfield из конфигурации (например, "Temperate Planet")
- `Position`: Нулевая позиция `(0, 0, 0)` — реальная позиция будет определена при материализации
- `FactionId`: ID фракции Zirax (обычно 2)
- `IsVirtual`: `true` — флаг виртуализации
- `Stage`: `ColonyStage.LandingPending` — начальная стадия
- `MainStructureId`: `null` — структура еще не заспавнена

**Логи:**
```
ColonyTickModule: creating initial virtual colony...
Creating new colony on 'Temperate Planet' at (0, 0, 0) [VIRTUAL]
Virtual colony {Id} initialized without structures
```

---

### 2. Развитие виртуальной колонии

Виртуальная колония обновляется каждый тик симуляции (`ColonyTickModule.OnSimulationUpdate`) так же, как материализованная, но **без физических операций** со структурами.

**Что работает для виртуальных колоний:**
- ✅ Производство ресурсов (`EconomySimulator.UpdateProduction`)
- ✅ Производство юнитов (`UnitEconomyManager.ProduceUnits`)
- ✅ Проверка возможности апгрейда (`StageManager.CanTransitionToNextStageAsync`)
- ✅ Переход на следующую стадию (`StageManager.TransitionToNextStageAsync`) — виртуально, без спавна структур
- ✅ Накопление ресурсов, юнитов, прогресс стадий

**Что НЕ работает для виртуальных колоний:**
- ❌ Спавн структур
- ❌ Спавн охранников и юнитов в игровой мир
- ❌ Защита структур от decay (`MaintainColonyStructuresAsync`)
- ❌ Проверка существования структур в мире

**Логи:**
```
ColonyManager: updating colony {Id} (Temperate Planet, stage=LandingPending, dt=1.00s) [VIRTUAL]
Colony {Id}: Transitioning from LandingPending to Base1 [VIRTUAL]
```

---

### 3. Материализация колонии (с retry-логикой)

Материализация происходит в два этапа для решения проблемы `PlayfieldConnectionNotFound`:

#### Этап 1: Пометка для материализации (IModApi.OnPlayfieldLoaded)

При загрузке playfield через событие `IModApi.Application.OnPlayfieldLoaded` вызывается `ColonyManager.EnsurePlayfieldColoniesSpawnedAsync`:

1. **Правильное событие**: `IModApi.OnPlayfieldLoaded` срабатывает когда playfield **полностью готов** для операций спавна (в отличие от `Event_Playfield_Loaded` который срабатывает слишком рано)
2. **Проверка виртуальности**: Если `colony.IsVirtual == true`
3. **Пометка для отложенного спавна**: 
   - `colony.PendingMaterialization = true`
   - `colony.MaterializationAttempts = 0`
4. **Без немедленного спавна** - используем retry-логику для надёжности

#### Этап 2: Retry-логика материализации (каждый тик)

В каждом тике симуляции `ColonyTickModule` вызывает `TryMaterializePendingColoniesAsync`:

1. **Поиск колоний с `PendingMaterialization == true`**

2. **Попытка материализации** (до 10 раз):
   - Счетчик `MaterializationAttempts++`
   - Вызов `StageManager.MaterializeColonyAsync(colony)`

3. **При успехе**:
   - `colony.IsVirtual = false`
   - `colony.PendingMaterialization = false`
   - `colony.MaterializationAttempts = 0`

4. **При ошибке** (PlayfieldConnectionNotFound):
   - Логирование попытки
   - Повтор в следующем тике (до 10 попыток)
   - После 10 неудачных попыток - отказ

**Процесс материализации (в StageManager):**

1. **Поиск подходящей позиции**: Используется `PlacementResolver.FindSuitableLocationAsync` для нахождения безопасной позиции:
   - Минимальное расстояние от игроков: `MinDistanceFromPlayers` (по умолчанию 500м)
   - Минимальное расстояние от структур игроков: `MinDistanceFromPlayerStructures` (по умолчанию 1000м)
   - Радиус поиска: `SearchRadius` (по умолчанию 2000м)

3. **Обновление позиции**: `colony.Position` обновляется на найденную позицию

4. **Спавн структуры**: В зависимости от текущей стадии колонии, спавнится соответствующий префаб:
   - `LandingPending`: DropShip (например, "BA_ConstructionSite")
   - `Base1`, `Base2`, и т.д.: Префабы из конфигурации стадий

5. **Обновление флагов**:
   - `colony.IsVirtual = false` — колония теперь материализована
   - `colony.MainStructureId = structureId` — сохраняем ID заспавненной структуры

6. **Сохранение state**: Изменения сохраняются в `state.json`

**Логи:**
```
# Пометка для материализации
EnsurePlayfieldColoniesSpawned: playfield 'Temperate Planet', 1 colony(ies)
Virtual colony {Id} marked for materialization (will retry in next ticks)

# Retry-попытки (каждый тик)
TryMaterializePendingColonies: 1 colony(ies) pending materialization
Attempting to materialize colony {Id} (attempt 1/10)
Materialization attempt 1 failed for colony {Id}: PlayfieldConnectionNotFound

Attempting to materialize colony {Id} (attempt 2/10)
Materializing virtual colony {Id} on 'Temperate Planet'...
Found suitable location for colony {Id} at (1234.5, 100.0, -567.8)
Colony {Id} materialized successfully with structure {structureId} at (1234.5, 100.0, -567.8)
✅ Colony {Id} materialized successfully on attempt 2
```

---

### 4. После материализации

После материализации колония работает как обычная:
- Структуры защищаются от decay (`MaintainColonyStructuresAsync`)
- Спавнятся охранники при апгрейдах
- Реагирует на разрушения структур
- Может создавать ресурсные аванпосты

---

## Архитектурная схема

```
┌─────────────────────────────────────────────────────────────────┐
│                  ИНИЦИАЛИЗАЦИЯ МОДА                              │
│                                                                   │
│  1. ModMain.InitializeAsync()                                    │
│  2. ColonyTickModule.InitializeAsync()                           │
│  3. if (state.Colonies.Count == 0 && EnableExpansion)           │
│     → CreateInitialVirtualColonyAsync()                          │
│                                                                   │
│  Результат: Виртуальная колония в БД                            │
│  - IsVirtual = true                                              │
│  - Position = (0, 0, 0)                                          │
│  - MainStructureId = null                                        │
│  - Stage = LandingPending                                        │
└───────────────────────┬─────────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────────┐
│                  СИМУЛЯЦИЯ (КАЖДЫЙ ТИК)                          │
│                                                                   │
│  SimulationEngine → ColonyTickModule.OnSimulationUpdate()        │
│                                                                   │
│  Для каждой колонии (включая виртуальные):                      │
│  - ColonyManager.UpdateColonyAsync()                             │
│    • EconomySimulator.UpdateProduction() ✅                      │
│    • UnitEconomyManager.ProduceUnits() ✅                        │
│    • StageManager.CanTransitionToNextStageAsync() ✅             │
│    • StageManager.TransitionToNextStageAsync() ✅ (виртуально)  │
│    • StageManager.MaintainColonyStructuresAsync() ❌ (skip)     │
│                                                                   │
│  Виртуальная колония развивается в БД без физического спавна   │
└───────────────────────┬─────────────────────────────────────────┘
                        │
                        │ Event_Playfield_Loaded
                        ▼
┌─────────────────────────────────────────────────────────────────┐
│              МАТЕРИАЛИЗАЦИЯ КОЛОНИИ                              │
│                                                                   │
│  1. Event_Playfield_Loaded("Temperate Planet")                  │
│  2. ColonyTickModule.OnGameEvent()                               │
│  3. ColonyManager.EnsurePlayfieldColoniesSpawnedAsync()          │
│  4. if (colony.IsVirtual):                                       │
│     → StageManager.MaterializeColonyAsync()                      │
│                                                                   │
│  Процесс материализации:                                         │
│  a) PlacementResolver.FindSuitableLocationAsync()                │
│     → Находит безопасную позицию                                 │
│  b) colony.Position = foundPosition                              │
│  c) EntitySpawner.SpawnStructureAtTerrainAsync()                 │
│     → Спавнит структуру соответствующей стадии                  │
│  d) colony.MainStructureId = structureId                         │
│  e) colony.IsVirtual = false                                     │
│  f) StateStore.SaveAsync(state)                                  │
│                                                                   │
│  Результат: Материализованная колония в мире                    │
└───────────────────────┬─────────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────────┐
│            РАБОТА МАТЕРИАЛИЗОВАННОЙ КОЛОНИИ                      │
│                                                                   │
│  - Все операции симуляции (ресурсы, юниты, апгрейды) ✅         │
│  - Защита структур от decay ✅                                   │
│  - Спавн охранников при апгрейдах ✅                             │
│  - Реакция на разрушения ✅                                      │
│  - Создание ресурсных аванпостов ✅                              │
└─────────────────────────────────────────────────────────────────┘
```

---

## Модели данных

### Colony.cs

```csharp
public class Colony
{
    /// <summary>
    /// Флаг виртуализации: колония существует в БД без физического спавна структур.
    /// </summary>
    [JsonProperty("IsVirtual")]
    public bool IsVirtual { get; set; }

    /// <summary>
    /// Флаг ожидания материализации: колония помечена для материализации, но playfield еще не готов.
    /// Используется для retry-логики при ошибке PlayfieldConnectionNotFound.
    /// </summary>
    [JsonProperty("PendingMaterialization")]
    public bool PendingMaterialization { get; set; }

    /// <summary>
    /// Количество попыток материализации (для retry-логики).
    /// Сбрасывается в 0 после успешной материализации.
    /// </summary>
    [JsonProperty("MaterializationAttempts")]
    public int MaterializationAttempts { get; set; }

    public Colony(string playfield, int factionId, Vector3 position, bool isVirtual = false)
    {
        Playfield = playfield;
        FactionId = factionId;
        Position = position;
        Stage = ColonyStage.LandingPending;
        CreatedAt = DateTime.UtcNow;
        ThreatLevel = 1;
        IsVirtual = isVirtual;
    }
}
```

### state.json

```json
{
  "Version": 1,
  "Colonies": [
    {
      "Id": "abc123",
      "Playfield": "Temperate Planet",
      "FactionId": 2,
      "Stage": "Base1",
      "Position": { "X": 0, "Y": 0, "Z": 0 },
      "MainStructureId": null,
      "IsVirtual": true,  // ← Виртуальная колония
      "PendingMaterialization": true,  // ← Ожидает материализации
      "MaterializationAttempts": 2,  // ← Попытка #2
      "Resources": {
        "Amount": 1500.5,
        "ProductionRate": 10.0
      },
      "UnitPool": {
        "Guards": { "Available": 5, "Active": 0, "Capacity": 10 }
      },
      "CreatedAt": "2026-02-05T10:00:00Z"
    }
  ]
}
```

---

## API

### IColonyManager

```csharp
/// <summary>
/// Создает новую колонию (может быть виртуальной)
/// </summary>
Task<Colony> CreateColonyAsync(string playfield, Vector3 position, int factionId, bool isVirtual = false);

/// <summary>
/// При загрузке playfield (через IModApi.OnPlayfieldLoaded): 
/// помечает виртуальные колонии для материализации (отложенный спавн).
/// </summary>
Task EnsurePlayfieldColoniesSpawnedAsync(string playfield);

/// <summary>
/// Пытается материализовать колонии с PendingMaterialization=true (retry-логика).
/// Вызывается каждый тик. Повторяет попытки до 10 раз при ошибке PlayfieldConnectionNotFound.
/// </summary>
Task TryMaterializePendingColoniesAsync();
```

### ModMain (IModApi Integration)

```csharp
// В IMod.Init(IModApi modAPI)
public void Init(IModApi modAPI)
{
    _extendedModApi = modAPI;
    
    // Подписка на правильное событие загрузки playfield
    modAPI.Application.OnPlayfieldLoaded += OnPlayfieldLoaded;
}

// Обработчик события
private void OnPlayfieldLoaded(IPlayfield playfield)
{
    var playfieldName = playfield.Name;
    
    // Получаем ColonyManager и запускаем материализацию
    var colonyManager = _container.Resolve<IColonyManager>();
    _ = Task.Run(() => colonyManager.EnsurePlayfieldColoniesSpawnedAsync(playfieldName));
}
```

### IStageManager

```csharp
/// <summary>
/// Инициализирует новую колонию (опционально виртуальную)
/// </summary>
Task<Colony> InitializeColonyAsync(string playfield, Vector3 position, int factionId, bool isVirtual = false);

/// <summary>
/// Материализует виртуальную колонию: находит позицию и спавнит структуры
/// </summary>
Task MaterializeColonyAsync(Colony colony);
```

---

## Конфигурация

### Configuration.json

```json
{
  "HomePlayfield": "Temperate Planet",
  "EnableExpansion": true,
  "Placement": {
    "MinDistanceFromPlayers": 500.0,
    "MinDistanceFromPlayerStructures": 1000.0,
    "SearchRadius": 2000.0,
    "MaxAttempts": 10
  },
  "Zirax": {
    "FactionId": 2,
    "Stages": [
      {
        "Stage": "LandingPending",
        "PrefabName": "BA_ConstructionSite",
        "RequiredResources": 0,
        "MinTimeSeconds": 0,
        "ProductionRate": 1.0,
        "GuardCount": 0
      },
      {
        "Stage": "Base1",
        "PrefabName": "BA_ZiraxMain",
        "RequiredResources": 1000,
        "MinTimeSeconds": 300,
        "ProductionRate": 5.0,
        "GuardCount": 2
      }
    ]
  }
}
```

---

## Тестирование

### Unit-тесты

```csharp
[Test]
public async Task CreateColonyAsync_WithVirtualFlag_CreatesVirtualColony()
{
    // Arrange
    var playfield = "Temperate Planet";
    var position = new Vector3(0, 0, 0);
    var factionId = 2;

    // Act
    var colony = await _colonyManager.CreateColonyAsync(playfield, position, factionId, isVirtual: true);

    // Assert
    Assert.IsTrue(colony.IsVirtual);
    Assert.IsNull(colony.MainStructureId);
    Assert.AreEqual(playfield, colony.Playfield);
    Assert.AreEqual(ColonyStage.LandingPending, colony.Stage);
}

[Test]
public async Task MaterializeColonyAsync_VirtualColony_SpawnsStructure()
{
    // Arrange
    var virtualColony = new Colony("Temperate Planet", 2, new Vector3(0, 0, 0), isVirtual: true);
    
    // Act
    await _stageManager.MaterializeColonyAsync(virtualColony);

    // Assert
    Assert.IsFalse(virtualColony.IsVirtual);
    Assert.IsNotNull(virtualColony.MainStructureId);
    Assert.AreNotEqual(new Vector3(0, 0, 0), virtualColony.Position); // Позиция обновлена
}
```

### Integration-тесты

```csharp
[Test]
public async Task ColonyTickModule_OnInitialize_CreatesVirtualColony()
{
    // Arrange: Пустой state
    var state = new SimulationState();

    // Act: Инициализация модуля
    await _colonyTickModule.InitializeAsync(state);

    // Assert: Виртуальная колония создана
    var savedState = await _stateStore.LoadAsync();
    Assert.AreEqual(1, savedState.Colonies.Count);
    Assert.IsTrue(savedState.Colonies[0].IsVirtual);
}

[Test]
public async Task EnsurePlayfieldColoniesSpawnedAsync_WithVirtualColony_MaterializesIt()
{
    // Arrange: Виртуальная колония в state
    var virtualColony = new Colony("Temperate Planet", 2, new Vector3(0, 0, 0), isVirtual: true);
    var state = new SimulationState();
    state.Colonies.Add(virtualColony);
    await _stateStore.SaveAsync(state);

    // Act: Загрузка playfield
    await _colonyManager.EnsurePlayfieldColoniesSpawnedAsync("Temperate Planet");

    // Assert: Колония материализована
    var updatedState = await _stateStore.LoadAsync();
    var colony = updatedState.Colonies.First();
    Assert.IsFalse(colony.IsVirtual);
    Assert.IsNotNull(colony.MainStructureId);
}
```

---

## Логирование

### Создание виртуальной колонии
```
INFO  | ColonyTickModule: creating initial virtual colony...
INFO  | Creating new colony on 'Temperate Planet' at (0, 0, 0) [VIRTUAL]
INFO  | Initializing new colony on 'Temperate Planet' at (0, 0, 0) [VIRTUAL]
INFO  | Virtual colony abc123 initialized without structures
INFO  | ColonyTickModule: created initial VIRTUAL colony abc123 on 'Temperate Planet'. Will be materialized when playfield loads.
```

### Обновление виртуальной колонии
```
DEBUG | ColonyManager: updating colony abc123 (Temperate Planet, stage=LandingPending, dt=1.00s) [VIRTUAL]
DEBUG | ColonyManager: updating colony abc123 (Temperate Planet, stage=Base1, dt=1.00s) [VIRTUAL]
INFO  | Colony abc123: Transitioning from LandingPending to Base1 [VIRTUAL]
```

### Материализация (через IModApi.OnPlayfieldLoaded)
```
# Событие загрузки playfield (IModApi)
INFO  | 🎯 IModApi.OnPlayfieldLoaded: 'Temperate Planet' is now READY for spawn operations
INFO  | EnsurePlayfieldColoniesSpawned: playfield 'Temperate Planet', 1 colony(ies)
INFO  | Virtual colony abc123 marked for materialization (will retry in next ticks)

# Retry-попытки (обычно успех с первой попытки)
DEBUG | TryMaterializePendingColonies: 1 colony(ies) pending materialization
DEBUG | Attempting to materialize colony abc123 (attempt 1/10)
INFO  | Materializing virtual colony abc123 on 'Temperate Planet'...
INFO  | Found suitable location for colony abc123 at (1234.5, 100.0, -567.8)
INFO  | Colony abc123 materialized successfully with structure 42 at (1234.5, 100.0, -567.8)
INFO  | ✅ Colony abc123 materialized successfully on attempt 1
```

---

## Преимущества виртуализации

1. **Надежность**: Не зависит от порядка событий загрузки playfield
2. **Производительность**: Нет спавна структур до появления игрока
3. **Консистентность**: Экономика работает непрерывно
4. **Масштабируемость**: Можно создавать множество виртуальных колоний на разных playfield без нагрузки на игровой мир
5. **Гибкость**: Легко добавить новые сценарии (например, создание колоний на недоступных playfield)

---

## Retry-логика материализации

### Зачем нужна retry-логика?

Хотя `IModApi.OnPlayfieldLoaded` срабатывает когда playfield готов, мы используем retry-логику для дополнительной надёжности и обработки edge-cases.

### Исправление проблемы PlayfieldConnectionNotFound

**Старый подход (❌ НЕ работал):**
- Использовался `Event_Playfield_Loaded` (через `CmdId.Event_Playfield_Loaded`)
- Срабатывал **слишком рано** - playfield начинал загружаться, но не был готов для спавна
- **Single-player проблема:** `Event_Player_ChangedPlayfield` не срабатывает в single-player режиме (баг Empyrion API)
- Результат: все попытки спавна провалились с `PlayfieldConnectionNotFound`

**Новый подход (✅ РАБОТАЕТ):**
- Используется `IModApi.Application.OnPlayfieldLoaded` (расширенный API)
- Срабатывает когда playfield **полностью готов** для всех операций
- **Работает в single-player и на dedicated server**
- Retry-логика нужна только для edge-cases и дополнительной надёжности

### Как работает retry-логика?

1. **При IModApi.OnPlayfieldLoaded:**
   - Playfield гарантированно готов, но мы всё равно помечаем колонию: `PendingMaterialization = true`
   - Это даёт дополнительную защиту от race conditions

2. **Каждый тик симуляции (1 раз в секунду):**
   - `TryMaterializePendingColoniesAsync` проверяет помеченные колонии
   - Пытается материализовать каждую (до 10 попыток)
   - При успехе — сбрасывает флаги и счетчик

3. **При неудаче (редко):**
   - `MaterializationAttempts++`
   - Логируется попытка (DEBUG уровень)
   - Повтор в следующем тике

4. **После 10 неудачных попыток:**
   - `PendingMaterialization = false` — отказ от материализации
   - Логируется предупреждение (WARN уровень)
   - Колония остается виртуальной

### Параметры retry
- **Максимум попыток:** 10
- **Интервал между попытками:** 1 секунда (частота тика симуляции)
- **Общее время ожидания:** до 10 секунд
- **Ожидаемые попытки:** 1-2 (playfield уже готов после OnPlayfieldLoaded)

## Известные ограничения

1. **Виртуальные колонии не отображаются на карте** до материализации
2. **Игрок не может атаковать виртуальные колонии** (структур нет в мире)
3. **Виртуальные охранники не патрулируют** (спавнятся только при материализации)
4. **Позиция виртуальной колонии `(0, 0, 0)`** не имеет смысла до материализации
5. **Материализация может занять до 10 секунд** при проблемах с playfield

---

## Будущие улучшения

1. **Частичная материализация**: Спавнить только главную структуру, добавлять аванпосты и охранников постепенно
2. **Ремиграция в виртуальность**: Если playfield долго не посещается, вернуть колонию в виртуальное состояние (despawn)
3. **Виртуальные угрозы**: Игрок может получать уведомления о росте виртуальных колоний
4. **Телепортация к виртуальным колониям**: При первом посещении playfield с виртуальной колонией показать игроку её местоположение

---

## См. также

- [Module 07: Colony Evolution](modules/Module_07_Colony_Evolution.md)
- [Module 04: Entity Spawner](modules/Module_04_Entity_Spawner.md)
- [Module 06: Placement Resolver](modules/Module_06_Placement_Resolver.md)
- [05_Схема_данных.md](05_Схема_данных.md)
