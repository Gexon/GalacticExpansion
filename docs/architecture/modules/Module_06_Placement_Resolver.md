# Модуль: Placement Resolver

**Назначение:** Поиск подходящих мест для спавна структур

---

## Ответственность

- Поиск свободных мест для размещения
- Проверка дистанций от игроков/структур
- Эвристика определения высоты поверхности
- Избегание spawn-protection зон
- Валидация найденных мест

---

## Интерфейсы

```csharp
public interface IPlacementResolver
{
    Task<PVector3> FindSuitableLocationAsync(PlacementCriteria criteria);
    Task<bool> IsLocationSuitableAsync(PVector3 position, PlacementCriteria criteria);
}

public class PlacementCriteria
{
    public string Playfield { get; set; }
    public float MinDistanceFromPlayers { get; set; }
    public float MinDistanceFromPlayerStructures { get; set; }
    public float PreferredAltitude { get; set; }
    public float SearchRadius { get; set; }
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
// Поиск места для базы
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
```

---

## Тесты

- `PlacementResolver_FindsSuitableLocation()`
- `PlacementResolver_RespectsMinimumDistances()`
- `PlacementResolver_ThrowsWhenNoLocationFound()`
