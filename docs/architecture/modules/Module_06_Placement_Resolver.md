# Модуль: Placement Resolver

**Назначение:** Поиск подходящих мест для спавна структур

---

## Ответственность

- Поиск свободных мест для размещения
- Проверка дистанций от игроков/структур
- **Точное определение высоты поверхности через `GetTerrainHeightAt()`** ✅ (v1.15+)
- Избегание spawn-protection зон
- Валидация найденных мест

---

## Интерфейсы

```csharp
public interface IPlacementResolver
{
    Task<PVector3> FindSuitableLocationAsync(PlacementCriteria criteria);
    Task<bool> IsLocationSuitableAsync(PVector3 position, PlacementCriteria criteria);
    
    // Новые методы (v1.15+)
    float GetTerrainHeight(IPlayfield playfield, float x, float z);
    Task<Vector3> FindLocationAtTerrainAsync(IPlayfield playfield, float x, float z, float heightOffset = 0);
}

public class PlacementCriteria
{
    public string Playfield { get; set; }
    public float MinDistanceFromPlayers { get; set; }
    public float MinDistanceFromPlayerStructures { get; set; }
    public float PreferredAltitude { get; set; }
    public float SearchRadius { get; set; }
    
    // Новые параметры (v1.15+)
    public bool UseTerrainHeight { get; set; } = true;  // Использовать GetTerrainHeightAt()
    public float HeightOffset { get; set; } = 0.5f;     // Отступ над землей
}
```

---

## Зависимости

- `IEmpyrionGateway` — для получения списка структур и игроков

---

## Ключевые классы

1. **PlacementResolver** — основной алгоритм
2. **SpiralSearchStrategy** — спиральный поиск
3. **PlacementValidator** — валидация мест

---

## Примеры использования

```csharp
// Поиск места для базы (классический способ)
var position = await _placementResolver.FindSuitableLocationAsync(
    new PlacementCriteria
    {
        Playfield = "Akua",
        MinDistanceFromPlayers = 500,
        MinDistanceFromPlayerStructures = 1000,
        PreferredAltitude = 150,
        SearchRadius = 2000
    }
);

// Получение точной высоты рельефа (v1.15+)
var playfield = _application.GetPlayfield("Akua");
float terrainHeight = _placementResolver.GetTerrainHeight(playfield, x: 1000, z: -500);

// Поиск места с автоматическим определением высоты (v1.15+)
var positionOnTerrain = await _placementResolver.FindLocationAtTerrainAsync(
    playfield: playfield,
    x: 1000,
    z: -500,
    heightOffset: 0.5f  // 0.5 метра над землей
);

// Поиск с использованием точного определения высоты (v1.15+)
var criteria = new PlacementCriteria
{
    Playfield = "Akua",
    MinDistanceFromPlayers = 500,
    MinDistanceFromPlayerStructures = 1000,
    SearchRadius = 2000,
    UseTerrainHeight = true,   // Использовать GetTerrainHeightAt()
    HeightOffset = 0.5f         // Отступ над землей
};

var accuratePosition = await _placementResolver.FindSuitableLocationAsync(criteria);
```

---

## Тесты

- `PlacementResolver_FindsSuitableLocation()`
- `PlacementResolver_RespectsMinimumDistances()`
- `PlacementResolver_ThrowsWhenNoLocationFound()`
