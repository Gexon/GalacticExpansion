# Модуль: Economy Simulator

**Приоритет разработки:** 2 (Высокий)  
**Зависимости:** Нет (независимый модуль)  
**Статус:** 🟡 В разработке

---

## 1. Назначение модуля

Economy Simulator управляет **виртуальной экономикой колоний**: производством ресурсов, их накоплением и потреблением для развития.

### Важно

Это **виртуальные ресурсы** (не настоящие игровые), используемые только для расчета прогрессии колоний.

---

## 2. Интерфейс

```csharp
/// <summary>
/// Симулятор экономики колоний
/// </summary>
public interface IEconomySimulator
{
    // === Производство ресурсов ===
    
    /// <summary>
    /// Обновление производства ресурсов для колонии
    /// Вызывается каждый тик симуляции (1 раз в секунду)
    /// </summary>
    /// <param name="colony">Колония</param>
    /// <param name="deltaTime">Время с последнего обновления (секунды)</param>
    void UpdateProduction(Colony colony, float deltaTime);
    
    // === Управление ресурсами ===
    
    /// <summary>
    /// Добавление аванпоста (увеличивает производство)
    /// </summary>
    void AddResourceNode(Colony colony, ResourceNode node);
    
    /// <summary>
    /// Удаление аванпоста
    /// </summary>
    void RemoveResourceNode(Colony colony, string nodeId);
    
    // === Проверки для апгрейда ===
    
    /// <summary>
    /// Проверка достаточности ресурсов для перехода на стадию
    /// </summary>
    bool HasEnoughResourcesForUpgrade(Colony colony, ColonyStage targetStage);
    
    /// <summary>
    /// Потребление ресурсов при апгрейде
    /// </summary>
    void ConsumeResourcesForUpgrade(Colony colony, ColonyStage targetStage);
    
    // === Статистика ===
    
    /// <summary>
    /// Расчет времени до следующего апгрейда
    /// </summary>
    TimeSpan GetTimeUntilNextUpgrade(Colony colony, ColonyStage targetStage);
}
```

---

## 3. Модели данных

```csharp
/// <summary>
/// Ресурсы колонии
/// </summary>
public class ColonyResources
{
    /// <summary>
    /// Текущие виртуальные ресурсы
    /// </summary>
    public int VirtualResources { get; set; }
    
    /// <summary>
    /// Скорость производства (ресурсов в секунду)
    /// </summary>
    public float ProductionRate { get; set; }
    
    /// <summary>
    /// Бонус производства от аванпостов (%)
    /// </summary>
    public float ProductionBonus { get; set; }
}

/// <summary>
/// Ресурсный аванпост
/// </summary>
public class ResourceNode
{
    public string Id { get; set; }
    public string Type { get; set; }  // "Iron", "Copper", etc.
    public Vector3 Position { get; set; }
    public int StructureId { get; set; }
    public float ProductionRate { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

---

## 4. Реализация

```csharp
/// <summary>
/// Реализация экономического симулятора
/// </summary>
public class EconomySimulator : IEconomySimulator
{
    private readonly ILogger<EconomySimulator> _logger;
    private readonly Dictionary<ColonyStage, int> _upgradeRequirements;
    
    public EconomySimulator(
        ILogger<EconomySimulator> logger,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Загрузка требований ресурсов из конфигурации
        _upgradeRequirements = LoadUpgradeRequirements(configuration);
    }
    
    /// <summary>
    /// Обновление производства ресурсов
    /// </summary>
    public void UpdateProduction(Colony colony, float deltaTime)
    {
        // Базовая скорость производства
        var baseProduction = colony.Resources.ProductionRate;
        
        // Бонус от аванпостов
        var bonus = 1.0f + (colony.Resources.ProductionBonus / 100f);
        
        // Расчет производства за deltaTime
        var production = baseProduction * bonus * deltaTime;
        
        // Добавление ресурсов
        colony.Resources.VirtualResources += (int)production;
        
        _logger.LogTrace(
            $"Colony {colony.Id}: Produced {production:F1} resources. " +
            $"Total={colony.Resources.VirtualResources}"
        );
    }
    
    /// <summary>
    /// Добавление аванпоста
    /// </summary>
    public void AddResourceNode(Colony colony, ResourceNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        
        colony.ResourceNodes.Add(node);
        
        // Увеличение бонуса производства (каждый аванпост +20%)
        colony.Resources.ProductionBonus += 20f;
        
        _logger.LogInformation(
            $"Colony {colony.Id}: Added resource node. " +
            $"Production bonus: {colony.Resources.ProductionBonus}%"
        );
    }
    
    /// <summary>
    /// Удаление аванпоста
    /// </summary>
    public void RemoveResourceNode(Colony colony, string nodeId)
    {
        var node = colony.ResourceNodes.FirstOrDefault(n => n.Id == nodeId);
        if (node == null)
        {
            _logger.LogWarning($"Resource node {nodeId} not found");
            return;
        }
        
        colony.ResourceNodes.Remove(node);
        
        // Уменьшение бонуса
        colony.Resources.ProductionBonus = Math.Max(0, colony.Resources.ProductionBonus - 20f);
        
        _logger.LogInformation(
            $"Colony {colony.Id}: Removed resource node. " +
            $"Production bonus: {colony.Resources.ProductionBonus}%"
        );
    }
    
    /// <summary>
    /// Проверка достаточности ресурсов
    /// </summary>
    public bool HasEnoughResourcesForUpgrade(Colony colony, ColonyStage targetStage)
    {
        if (!_upgradeRequirements.TryGetValue(targetStage, out var required))
        {
            _logger.LogWarning($"No upgrade requirements defined for stage {targetStage}");
            return false;
        }
        
        var hasEnough = colony.Resources.VirtualResources >= required;
        
        _logger.LogDebug(
            $"Colony {colony.Id}: Resources check for {targetStage}: " +
            $"Current={colony.Resources.VirtualResources}, Required={required}, " +
            $"HasEnough={hasEnough}"
        );
        
        return hasEnough;
    }
    
    /// <summary>
    /// Потребление ресурсов при апгрейде
    /// </summary>
    public void ConsumeResourcesForUpgrade(Colony colony, ColonyStage targetStage)
    {
        if (!_upgradeRequirements.TryGetValue(targetStage, out var required))
        {
            throw new InvalidOperationException($"No upgrade requirements for {targetStage}");
        }
        
        if (colony.Resources.VirtualResources < required)
        {
            throw new InvalidOperationException(
                $"Not enough resources: {colony.Resources.VirtualResources} < {required}"
            );
        }
        
        colony.Resources.VirtualResources -= required;
        
        _logger.LogInformation(
            $"Colony {colony.Id}: Consumed {required} resources for upgrade to {targetStage}. " +
            $"Remaining={colony.Resources.VirtualResources}"
        );
    }
    
    /// <summary>
    /// Расчет времени до апгрейда
    /// </summary>
    public TimeSpan GetTimeUntilNextUpgrade(Colony colony, ColonyStage targetStage)
    {
        if (!_upgradeRequirements.TryGetValue(targetStage, out var required))
        {
            return TimeSpan.MaxValue;
        }
        
        var remaining = required - colony.Resources.VirtualResources;
        if (remaining <= 0)
        {
            return TimeSpan.Zero;  // Уже достаточно
        }
        
        // Расчет производства в секунду
        var productionPerSecond = colony.Resources.ProductionRate * 
                                  (1.0f + colony.Resources.ProductionBonus / 100f);
        
        if (productionPerSecond <= 0)
        {
            return TimeSpan.MaxValue;  // Производство отсутствует
        }
        
        var secondsNeeded = remaining / productionPerSecond;
        
        return TimeSpan.FromSeconds(secondsNeeded);
    }
    
    // === ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ===
    
    private Dictionary<ColonyStage, int> LoadUpgradeRequirements(IConfiguration configuration)
    {
        // Загрузка из Configuration.json секции "Zirax.Stages"
        var stages = configuration
            .GetSection("Zirax:Stages")
            .Get<List<StageConfiguration>>() ?? new List<StageConfiguration>();
        
        return stages.ToDictionary(
            s => s.Stage,
            s => s.RequiredResources
        );
    }
}
```

---

## 5. Использование в Core Loop

```csharp
public class SimulationEngine
{
    private readonly IEconomySimulator _economySimulator;
    
    public void OnSimulationTick(object state)
    {
        var deltaTime = 1.0f;  // 1 секунда
        
        foreach (var colony in _state.Colonies)
        {
            // Обновляем производство
            _economySimulator.UpdateProduction(colony, deltaTime);
            
            // Проверяем возможность апгрейда
            var nextStage = _stageManager.GetNextStage(colony.Stage);
            if (nextStage.HasValue)
            {
                if (_economySimulator.HasEnoughResourcesForUpgrade(colony, nextStage.Value))
                {
                    var timeRemaining = _economySimulator.GetTimeUntilNextUpgrade(colony, nextStage.Value);
                    _logger.LogInformation(
                        $"Colony {colony.Id} can upgrade to {nextStage} now!"
                    );
                }
            }
        }
    }
}
```

---

## 6. Примеры расчетов

### 6.1 Базовое производство

```
Colony Stage: BaseL1
ProductionRate: 150 ресурсов/секунду
ResourceNodes: 0
ProductionBonus: 0%

Производство за 1 секунду = 150 * (1 + 0/100) = 150 ресурсов
```

### 6.2 С аванпостами

```
Colony Stage: BaseL2
ProductionRate: 250 ресурсов/секунду
ResourceNodes: 2
ProductionBonus: 40% (2 * 20%)

Производство за 1 секунду = 250 * (1 + 40/100) = 350 ресурсов
```

### 6.3 Время до апгрейда

```
Текущие ресурсы: 1500
Требуется для BaseL3: 6000
Недостаток: 4500

Производство: 350 ресурсов/сек
Время до апгрейда: 4500 / 350 = 12.86 сек (~13 секунд)
```

---

## 7. Балансировка

### 7.1 Рекомендуемые значения

| Стадия | ProductionRate | RequiredResources | Время накопления* |
|--------|---------------|-------------------|-------------------|
| ConstructionYard | 100/сек | 0 | 0 (стартовая) |
| BaseL1 | 150/сек | 1000 | ~7 минут |
| BaseL2 | 250/сек | 3000 | ~20 минут |
| BaseL3 | 400/сек | 6000 | ~25 минут |
| BaseMax | 500/сек | 10000 | ~33 минуты |

*При наличии 2 аванпостов (+40% бонус)

---

## 8. Тестирование

```csharp
[Fact]
public void UpdateProduction_IncreasesResources()
{
    // Arrange
    var colony = new Colony
    {
        Resources = new ColonyResources
        {
            VirtualResources = 100,
            ProductionRate = 50,
            ProductionBonus = 0
        }
    };
    
    var simulator = new EconomySimulator(_logger, _config);
    
    // Act
    simulator.UpdateProduction(colony, deltaTime: 10.0f);  // 10 секунд
    
    // Assert
    Assert.Equal(600, colony.Resources.VirtualResources);  // 100 + 50*10
}

[Fact]
public void AddResourceNode_IncreasesProductionBonus()
{
    // Arrange
    var colony = CreateTestColony();
    var simulator = new EconomySimulator(_logger, _config);
    
    // Act
    simulator.AddResourceNode(colony, new ResourceNode
    {
        Id = "node1",
        Type = "Iron",
        ProductionRate = 0
    });
    
    // Assert
    Assert.Equal(20f, colony.Resources.ProductionBonus);
    Assert.Single(colony.ResourceNodes);
}
```

---

## 9. Чеклист разработчика

**Этап 1: Базовое производство (1 день)**
- [ ] Реализовать `IEconomySimulator`
- [ ] `UpdateProduction()` с deltaTime
- [ ] Unit-тесты для производства

**Этап 2: Аванпосты (1 день)**
- [ ] `AddResourceNode()` / `RemoveResourceNode()`
- [ ] Система бонусов
- [ ] Тесты

**Этап 3: Проверки апгрейда (0.5 дня)**
- [ ] `HasEnoughResourcesForUpgrade()`
- [ ] `ConsumeResourcesForUpgrade()`
- [ ] Загрузка требований из конфига

**Этап 4: Статистика (0.5 дня)**
- [ ] `GetTimeUntilNextUpgrade()`
- [ ] Балансировка значений

---

## 10. Балансировка производства

### Формула производства

```
Actual Production = BaseRate × (1 + Bonus/100) × deltaTime

где:
- BaseRate — ProductionRate из конфигурации стадии
- Bonus — ProductionBonus (20% за каждый аванпост)
- deltaTime — время в секундах
```

### Рекомендации

1. **Не делать производство слишком быстрым** — игрок должен видеть прогрессию
2. **Аванпосты должны быть значимыми** — 20% бонус = заметное ускорение
3. **Учитывать минимальное время стадии** — ресурсов может хватить раньше, чем пройдет MinTime

---

## 11. Связь с другими документами

- **[Module_07_Colony_Evolution.md](Module_07_Colony_Evolution.md)** — использует Economy для проверки условий апгрейда
- **[02_Архитектурный_план.md](../02_Архитектурный_план.md)** — общая архитектура экономики

---

**Последнее обновление:** 28.01.2026
