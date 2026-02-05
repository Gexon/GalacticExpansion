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

### 3. Материализация колонии (из Game_Update в PfServer процессе)

Материализация решает проблему многопроцессной архитектуры Empyrion:

**Критическая проблема архитектуры:**
- Мод загружается в **ДВА процесса**: `Dedi` (главный сервер) и `PfServer` (playfield сервер)
- Spawn-операции работают **ТОЛЬКО из PfServer процесса** (у которого есть playfield connection)
- `SimulationEngine` работает в `Task.Run` в **Dedi процессе** → `PlayfieldConnectionNotFound`
- **Решение**: вызывать материализацию из `Game_Update()` (правильный поток в PfServer)

#### Этап 1: Пометка для материализации (Event_Playfield_Loaded)

При загрузке playfield через событие `Event_Playfield_Loaded` вызывается `ColonyManager.EnsurePlayfieldColoniesSpawnedAsync`:

1. **Event_Playfield_Loaded** срабатывает когда playfield **начинает загружаться**
2. **Проверка виртуальности**: Если `colony.IsVirtual == true`
3. **Пометка для отложенного спавна**: 
   - `colony.PendingMaterialization = true`
   - `colony.MaterializationAttempts = 0` (начинаем с 0)
4. **Без немедленного спавна** - используем retry-логику из `Game_Update()`

#### Этап 2: Retry-логика материализации (из ModMain.Game_Update)

**КРИТИЧНО**: Вызывается из `ModMain.Game_Update()` (НЕ из SimulationEngine!)

1. **Поиск колоний с `PendingMaterialization == true`**

2. **Активная проверка готовности плейфилда** через `IsPlayfieldReadyAsync`:
   - Делается запрос `Request_Playfield_Stats` (работает только когда playfield загружен)
   - Таймаут 2 секунды
   - Если запрос успешен → плейфилд готов, переход к шагу 3
   - Если ошибка → плейфилд ещё загружается, повтор в следующем Game_Update

3. **Попытка материализации** (когда плейфилд готов, до 100 попыток):
   - Счетчик `MaterializationAttempts++` (1, 2, 3... до 100)
   - Вызов `StageManager.MaterializeColonyAsync(colony)`
   - **ВАЖНО**: `EntitySpawnInfo.playfield` заполняется правильно!

4. **При успехе**:
   - `colony.IsVirtual = false`
   - `colony.PendingMaterialization = false`
   - `colony.MaterializationAttempts = 0`

5. **При ошибке** (любая ошибка во время материализации):
   - Логирование попытки
   - Повтор в следующем Game_Update (каждую секунду)
   - После 100 неудачных попыток - отказ

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
Virtual colony {Id} marked for materialization (playfield readiness will be checked)

# Проверка готовности плейфилда (из Game_Update в PfServer процессе)
TryMaterializePendingColonies: 1 colony(ies) pending materialization
Checking playfield 'Temperate Planet' readiness for colony {Id} (attempt 1/100)
Playfield 'Temperate Planet' not ready yet: PlayfieldOfPlayerNotFound
[Повтор в следующем Game_Update - ~1 секунда]

Checking playfield 'Temperate Planet' readiness for colony {Id} (attempt 2/100)
Playfield 'Temperate Planet' is READY (Request_Playfield_Stats succeeded)
Attempting to materialize colony {Id} (attempt 2/100)
Materializing virtual colony {Id} on 'Temperate Planet'...
Found suitable location for colony {Id} at (1234.5, 100.0, -567.8)
Spawning structure 'BA_ConstructionSite' at (1234.5, 100.0, -567.8) on playfield 'Temperate Planet'
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

### ColonyTickModule

```csharp
// Подписка на Event_Playfield_Loaded
public async Task InitializeAsync(SimulationState state)
{
    // ... создание виртуальной колонии ...
    
    _gateway.GameEventReceived += OnGameEvent;
}

// Обработчик события
private void OnGameEvent(object? sender, GameEventArgs e)
{
    if (e.EventId != CmdId.Event_Playfield_Loaded)
        return;

    var playfieldName = GetPlayfieldNameFromEventData(e.Data);
    
    // Запускаем материализацию с задержкой
    _ = Task.Run(() => _colonyManager.EnsurePlayfieldColoniesSpawnedAsync(playfieldName));
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

### Материализация (Event_Playfield_Loaded + задержка 3 секунды)
```
# Событие загрузки playfield
INFO  | ColonyTickModule: Playfield_Loaded 'Temperate Planet' - ensuring colonies on playfield
INFO  | EnsurePlayfieldColoniesSpawned: playfield 'Temperate Planet', 1 colony(ies)
INFO  | Virtual colony abc123 marked for materialization (will retry after 3s delay)

# Задержка 3 секунды (ожидание готовности playfield)
DEBUG | TryMaterializePendingColonies: 1 colony(ies) pending materialization
DEBUG | Colony abc123 waiting... (3s remaining)
DEBUG | Colony abc123 waiting... (2s remaining)
DEBUG | Colony abc123 waiting... (1s remaining)

# Retry-попытки материализации (после задержки)
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

## Retry-логика материализации с задержкой

### Зачем нужна задержка?

Проблема `PlayfieldConnectionNotFound` возникает потому что `Event_Playfield_Loaded` срабатывает когда playfield **начинает загружаться**, но ещё **НЕ готов** для операций спавна через ModAPI.

### Исправление проблемы PlayfieldConnectionNotFound

**Старый подход (❌ НЕ работал):**
- Немедленная материализация при `Event_Playfield_Loaded`
- Результат: все попытки спавна провалились с `PlayfieldConnectionNotFound`

**Попытка использовать IModApi.OnPlayfieldLoaded (❌ НЕ сработало):**
- Событие `IModApi.Application.OnPlayfieldLoaded` теоретически правильное
- **НО** в local dedicated режиме playfield запускается в **отдельном процессе**
- Событие срабатывает в контексте playfield процесса, а НЕ в dedicated server процессе
- Мод инициализирован в dedicated процессе → событие не доходит

**Новый подход (✅ РАБОТАЕТ):**
- Используем `Event_Playfield_Loaded` (доступно в dedicated процессе)
- **Добавляем задержку 3 секунды** перед первой попыткой материализации
- Retry-логика с до 10 попыток после задержки
- Итого: 3с задержка + 10с максимум попыток = до 13 секунд

### Как работает retry-логика с задержкой?

1. **При Event_Playfield_Loaded:**
   - Playfield начал загружаться (ещё не готов)
   - Помечаем колонию: `PendingMaterialization = true`
   - Устанавливаем задержку: `MaterializationAttempts = -3` (3 секунды)

2. **Задержка (первые 3 тика):**
   - `MaterializationAttempts < 0` → пропускаем попытку материализации
   - `MaterializationAttempts++` → `-3 → -2 → -1 → 0`
   - Логируется оставшееся время ожидания

3. **Попытки материализации (после задержки, до 10 раз):**
   - `MaterializationAttempts >= 0` → пытаемся материализовать
   - При успехе — сбрасываем флаги
   - При ошибке — повтор в следующем тике

4. **После 10 неудачных попыток:**
   - `PendingMaterialization = false` — отказ
   - Колония остается виртуальной

### Параметры retry
- **Начальная задержка:** 3 секунды (ожидание готовности playfield)
- **Максимум попыток:** 10 (после задержки)
- **Интервал между попытками:** 1 секунда
- **Общее время ожидания:** до 13 секунд (3с задержка + 10с попытки)

## Известные ограничения

1. **Виртуальные колонии не отображаются на карте** до материализации
2. **Игрок не может атаковать виртуальные колонии** (структур нет в мире)
3. **Виртуальные охранники не патрулируют** (спавнятся только при материализации)
4. **Позиция виртуальной колонии `(0, 0, 0)`** не имеет смысла до материализации
5. **Задержка материализации:** минимум 3 секунды после входа игрока на playfield
6. **Максимальное время материализации:** до 13 секунд (3с задержка + 10с попыток)

## Технические детали решения проблемы PlayfieldConnectionNotFound

### Почему IModApi.OnPlayfieldLoaded не сработало?

В **local dedicated режиме** (запуск dedicated server локально на ПК игрока):
- Playfield серверы запускаются как **отдельные процессы** (`Empyrion.exe -playfieldserver`)
- Событие `IModApi.Application.OnPlayfieldLoaded` срабатывает в контексте **playfield процесса**
- Мод инициализирован в **dedicated процессе** → событие не доходит
- Результат: подписка успешна (`✅ Subscribed`), но обработчик никогда не вызывается

### Почему материализация из Game_Update() работает?

**Критическая архитектурная особенность Empyrion:**

1. **Многопроцессная архитектура** (`Info.yaml.txt`, `ModTargets`):
   - `Dedi` = Dedicated server Main process (chat, API1, основная логика)
   - `PfServer` = Playfield process (**Entity info, spawning**)
   - Мод загружается **в оба процесса** если указан `ModTargets: Dedi, PfServer`

2. **Проблема PlayfieldConnectionNotFound:**
   - Spawn-операции работают **ТОЛЬКО из PfServer процесса**
   - `SimulationEngine` работает в `Task.Run` в **Dedi процессе** → нет playfield connection
   - Результат: `PlayfieldConnectionNotFound` даже если playfield загружен

3. **Решение - Game_Update():**
   - `Game_Update()` вызывается игрой в **правильном контексте потока**
   - В PfServer процессе → есть доступ к playfield connections
   - `Request_Playfield_Stats` проверяет готовность (работает только когда playfield загружен)
   - Retry до 100 попыток (100 секунд) для максимальной надёжности
   - **Работает в local dedicated и на full dedicated server**

4. **Дополнительно - EntitySpawnInfo.playfield:**
   - Поле `playfield` в `EntitySpawnInfo` ОБЯЗАТЕЛЬНО заполняется
   - Указывает игре "на каком playfield спавнить"
   - Без этого поля spawn может пойти не на тот playfield или упасть

### Известные баги Empyrion (НЕ связаны с модом)

**1. NullReferenceException в PaneList.RemoveEditor:**
- Краш dedicated server при старте
- Происходит в `Assembly-CSharp.PaneList.RemoveEditor()`
- Баг самого Empyrion, не связан с модом
- Обходное решение: перезапуск сервера

**2. Event_Player_ChangedPlayfield не работает в single-player:**
- Подтверждённый баг Empyrion API (по словам разработчика мода EmpyrionScripting)
- Событие не срабатывает в single-player режиме
- На dedicated server работает нормально
- Поэтому используем `Event_Playfield_Loaded` вместо событий игрока

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
