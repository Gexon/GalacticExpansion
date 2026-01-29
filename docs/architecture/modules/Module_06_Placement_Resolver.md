# Модуль: Placement Resolver

**Приоритет разработки:** 2 (Высокий)  
**Зависимости:** Module_02 (EmpyrionGateway)  
**Статус:** 🟢 Спецификация готова

---

## 1. Назначение

Placement Resolver находит **подходящие места для спавна структур**, проверяя дистанции от игроков/структур и **точно определяя высоту рельефа** через `IPlayfield.GetTerrainHeightAt()` (v1.15+).

**Ключевая проблема, которую решает:** Структуры больше не спавнятся под землей или в воздухе благодаря точному API определения высоты.

---

## 2. Интерфейсы

```csharp
/// <summary>
/// Поиск мест для размещения структур
/// </summary>
public interface IPlacementResolver
{
    /// <summary>
    /// Поиск подходящего места согласно критериям
    /// </summary>
    Task<Vector3> FindSuitableLocationAsync(PlacementCriteria criteria);
    
    /// <summary>
    /// Проверка пригодности конкретного места
    /// </summary>
    Task<bool> IsLocationSuitableAsync(Vector3 position, PlacementCriteria criteria);
    
    // === Новые методы (v1.15+) ===
    
    /// <summary>
    /// Точное определение высоты рельефа (решена проблема с эвристиками!)
    /// </summary>
    float GetTerrainHeight(IPlayfield playfield, float x, float z);
    
    /// <summary>
    /// Поиск места на рельефе с указанным offset'ом
    /// </summary>
    Task<Vector3> FindLocationAtTerrainAsync(
        string playfield, 
        float x, 
        float z, 
        float heightOffset = 0.5f
    );
}

/// <summary>
/// Критерии размещения
/// </summary>
public class PlacementCriteria
{
    public string Playfield { get; set; }
    public float MinDistanceFromPlayers { get; set; } = 500f;
    public float MinDistanceFromPlayerStructures { get; set; } = 1000f;
    public float SearchRadius { get; set; } = 2000f;
    
    // Новые параметры (v1.15+)
    public bool UseTerrainHeight { get; set; } = true;  // Использовать GetTerrainHeightAt()
    public float HeightOffset { get; set; } = 0.5f;     // Отступ над землей (метры)
    
    // Опционально
    public Vector3? PreferredLocation { get; set; }     // Предпочитаемая точка (центр поиска)
    public int FactionId { get; set; }                  // Для фильтрации структур
}
```

---

## 3. Алгоритм спирального поиска

```csharp
public async Task<Vector3> FindSuitableLocationAsync(PlacementCriteria criteria)
{
    var center = criteria.PreferredLocation ?? Vector3.Zero;
    var searchRadius = criteria.SearchRadius;
    var stepSize = 50f;  // Шаг поиска (метры)
    
    // Получение списка существующих структур
    var structures = await _gateway.SendRequestAsync<List<GlobalStructureInfo>>(
        CmdId.Request_GlobalStructure_List, 
        null
    );
    
    // Спиральный поиск от центра
    for (float radius = 0; radius < searchRadius; radius += stepSize)
    {
        var angleStep = stepSize / Math.Max(radius, 1);  // Адаптивный шаг угла
        
        for (float angle = 0; angle < 2 * Math.PI; angle += angleStep)
        {
            var testX = center.X + radius * Math.Cos(angle);
            var testZ = center.Z + radius * Math.Sin(angle);
            
            // Определение высоты рельефа (v1.15+)
            Vector3 testPosition;
            if (criteria.UseTerrainHeight)
            {
                testPosition = await FindLocationAtTerrainAsync(
                    criteria.Playfield,
                    testX,
                    testZ,
                    criteria.HeightOffset
                );
            }
            else
            {
                testPosition = new Vector3(testX, criteria.PreferredAltitude, testZ);
            }
            
            // Проверка пригодности
            if (await IsLocationSuitableAsync(testPosition, structures, criteria))
            {
                _logger.LogInformation($"Found suitable location: {testPosition}");
                return testPosition;
            }
        }
    }
    
    throw new PlacementException("No suitable location found in search radius");
}
```

---

## 4. Точное определение высоты (v1.15+)

```csharp
public float GetTerrainHeight(IPlayfield playfield, float x, float z)
{
    // НОВЫЙ API v1.15+ - точное определение высоты рельефа!
    return playfield.GetTerrainHeightAt(x, z);
}

public async Task<Vector3> FindLocationAtTerrainAsync(
    string playfieldName, 
    float x, 
    float z, 
    float heightOffset = 0.5f)
{
    // Получение IPlayfield через Gateway/Application
    var playfield = _application.GetPlayfield(playfieldName);
    
    if (playfield == null)
        throw new ArgumentException($"Playfield {playfieldName} not found");
    
    // Точная высота рельефа
    float terrainHeight = GetTerrainHeight(playfield, x, z);
    
    return new Vector3(x, terrainHeight + heightOffset, z);
}
```

---

## 5. Валидация места

```csharp
private async Task<bool> IsLocationSuitableAsync(
    Vector3 position, 
    List<GlobalStructureInfo> structures,
    PlacementCriteria criteria)
{
    // === ПРОВЕРКА 1: Дистанция от структур ===
    foreach (var structure in structures.Where(s => s.playfield == criteria.Playfield))
    {
        var distance = Vector3.Distance(position, structure.pos.ToVector3());
        
        // Проверка дистанции от структур игроков
        if (structure.factionId != criteria.FactionId && 
            distance < criteria.MinDistanceFromPlayerStructures)
        {
            return false;
        }
    }
    
    // === ПРОВЕРКА 2: Дистанция от игроков ===
    if (criteria.MinDistanceFromPlayers > 0)
    {
        var players = _playerTracker.GetPlayersOnPlayfield(criteria.Playfield);
        foreach (var player in players)
        {
            var distance = Vector3.Distance(position, player.Position);
            if (distance < criteria.MinDistanceFromPlayers)
            {
                return false;
            }
        }
    }
    
    // === ПРОВЕРКА 3: Spawn-protection зоны ===
    // TODO: Проверка через конфигурацию сервера
    
    return true;
}
```

---

## 6. Примеры использования

### 6.1 Поиск места для базы

```csharp
// Автоматический поиск с точной высотой (v1.15+)
var position = await _placementResolver.FindSuitableLocationAsync(
    new PlacementCriteria
    {
        Playfield = "Akua",
        MinDistanceFromPlayers = 500f,
        MinDistanceFromPlayerStructures = 1000f,
        SearchRadius = 2000f,
        UseTerrainHeight = true,   // Точная высота
        HeightOffset = 0.5f         // 0.5м над землей
    }
);

// Спавн базы на найденном месте
await _entitySpawner.SpawnStructureAsync("GLEX_Base_L1", position, ...);
```

### 6.2 Размещение на конкретных координатах

```csharp
// Проверка конкретного места
var testPosition = new Vector3(1000, 0, -500);
var position = await _placementResolver.FindLocationAtTerrainAsync(
    "Akua",
    testPosition.X,
    testPosition.Z,
    heightOffset: 1.0f  // 1 метр над землей
);

// Проверка пригодности
if (await _placementResolver.IsLocationSuitableAsync(position, criteria))
{
    // Место подходит
}
```

---

## 7. Обработка ошибок

| Ошибка | Причина | Решение |
|--------|---------|---------|
| **PlacementException** | Не найдено место в радиусе | Увеличить SearchRadius |
| **PlayfieldNotFound** | Playfield не существует | Проверить название |
| **InvalidCoordinates** | Координаты вне границ мира | Валидация входных данных |

---

## 8. Чеклист разработчика

**Этап 1: Базовый поиск (1 день)**
- [ ] Реализовать `IPlacementResolver`
- [ ] Спиральный алгоритм поиска
- [ ] Валидация дистанций
- [ ] Unit-тесты

**Этап 2: Точная высота (0.5 дня)**
- [ ] Интеграция с `IPlayfield.GetTerrainHeightAt()` (v1.15+)
- [ ] `FindLocationAtTerrainAsync()`
- [ ] Тесты на разном рельефе

**Этап 3: Оптимизация (0.5 дня)**
- [ ] Кэширование списка структур
- [ ] Параллельная проверка мест
- [ ] Performance тесты

---

## 9. Известные проблемы

### 9.1 Структуры спавнятся под землей/в воздухе

**Причина (старая):** Использовались эвристики для высоты

**Решение (v1.15+):** Использовать `GetTerrainHeightAt()` — точный API

### 9.2 Долгий поиск при большом радиусе

**Причина:** Проверка каждой точки занимает время

**Решение:** Использовать адаптивный шаг (больше при большом радиусе)

---

## 10. Производительность

**Метрики:**
- Поиск места: 100-500 мс (зависит от радиуса)
- Проверка одного места: 1-5 мс
- Max проверок за поиск: ~100-200

**Оптимизации:**
- Кэширование списка структур (обновлять раз в 10с)
- Ранний выход при нахождении места
- Параллельная проверка нескольких точек

---

## 11. Связь с другими документами

- **[Module_04_Entity_Spawner.md](Module_04_Entity_Spawner.md)** — использует Placement Resolver для спавна
- **[Module_02_EmpyrionGateway.md](Module_02_EmpyrionGateway.md)** — `GetTerrainHeightAt()` через IPlayfieldOperations

---

**Последнее обновление:** 28.01.2026  
**Размер:** ~360 строк
