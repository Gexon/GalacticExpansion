# Модуль: Structure Tracker

**Приоритет разработки:** 3 (Средний)  
**Зависимости:** Module_02 (EmpyrionGateway)  
**Статус:** 🟡 В разработке

---

## 1. Назначение модуля

Structure Tracker отслеживает структуры на сервере и детектирует их создание/уничтожение, предоставляя информацию другим модулям для реакции на изменения.

### Ключевая функция

**Детектирование разрушения колоний** — при уничтожении базы игроками, Colony Evolution должна откатить колонию на предыдущую стадию.

---

## 2. Интерфейс

```csharp
/// <summary>
/// Трекер структур на сервере
/// </summary>
public interface IStructureTracker
{
    // === Запросы ===
    
    /// <summary>
    /// Получение всех структур на playfield
    /// </summary>
    List<GlobalStructureInfo> GetStructuresOnPlayfield(
        string playfield, 
        int? factionId = null
    );
    
    /// <summary>
    /// Получение структуры по EntityId
    /// </summary>
    GlobalStructureInfo GetStructure(int entityId);
    
    /// <summary>
    /// Проверка существования структуры
    /// </summary>
    bool StructureExists(int entityId);
    
    // === Обновление данных ===
    
    /// <summary>
    /// Обновление списка структур с сервера
    /// Вызывается периодически (раз в 10 секунд)
    /// </summary>
    Task RefreshStructuresAsync();
    
    // === События ===
    
    /// <summary>
    /// Структура уничтожена
    /// </summary>
    event EventHandler<StructureEventArgs> StructureDestroyed;
    
    /// <summary>
    /// Структура создана
    /// </summary>
    event EventHandler<StructureEventArgs> StructureCreated;
    
    // === Управление ===
    
    void Start();
    void Stop();
}
```

---

## 3. Модели данных

```csharp
/// <summary>
/// Событие изменения структуры
/// </summary>
public class StructureEventArgs : EventArgs
{
    public GlobalStructureInfo Structure { get; set; }
    public DateTime Timestamp { get; set; }
}
```

---

## 4. Реализация

```csharp
/// <summary>
/// Реализация трекера структур
/// </summary>
public class StructureTracker : IStructureTracker
{
    private readonly IEmpyrionGateway _gateway;
    private readonly ILogger<StructureTracker> _logger;
    
    // Кэш структур: EntityId → GlobalStructureInfo
    private readonly ConcurrentDictionary<int, GlobalStructureInfo> _structures;
    
    // Индекс по playfield
    private readonly ConcurrentDictionary<string, HashSet<int>> _structuresByPlayfield;
    
    // Таймер периодического обновления
    private Timer _refreshTimer;
    private const int RefreshIntervalSeconds = 10;
    
    public event EventHandler<StructureEventArgs> StructureDestroyed;
    public event EventHandler<StructureEventArgs> StructureCreated;
    
    public StructureTracker(
        IEmpyrionGateway gateway,
        ILogger<StructureTracker> logger)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _structures = new ConcurrentDictionary<int, GlobalStructureInfo>();
        _structuresByPlayfield = new ConcurrentDictionary<string, HashSet<int>>();
    }
    
    /// <summary>
    /// Запуск трекера
    /// </summary>
    public void Start()
    {
        _logger.LogInformation("Starting Structure Tracker...");
        
        // Первоначальная загрузка структур
        _ = Task.Run(async () => await RefreshStructuresAsync());
        
        // Запуск таймера периодического обновления
        _refreshTimer = new Timer(
            async _ => await RefreshStructuresAsync(),
            null,
            TimeSpan.FromSeconds(RefreshIntervalSeconds),
            TimeSpan.FromSeconds(RefreshIntervalSeconds)
        );
        
        _logger.LogInformation("Structure Tracker started");
    }
    
    /// <summary>
    /// Остановка трекера
    /// </summary>
    public void Stop()
    {
        _logger.LogInformation("Stopping Structure Tracker...");
        
        _refreshTimer?.Dispose();
        _structures.Clear();
        _structuresByPlayfield.Clear();
        
        _logger.LogInformation("Structure Tracker stopped");
    }
    
    /// <summary>
    /// Обновление списка структур
    /// </summary>
    public async Task RefreshStructuresAsync()
    {
        try
        {
            _logger.LogDebug("Refreshing structures list...");
            
            // Запрос всех структур с сервера
            var allStructures = await _gateway.SendRequestAsync<List<GlobalStructureInfo>>(
                CmdId.Request_GlobalStructure_List,
                null,
                timeoutMs: 5000
            );
            
            if (allStructures == null || !allStructures.Any())
            {
                _logger.LogWarning("No structures returned from server");
                return;
            }
            
            // Создаем новый индекс
            var newStructures = new Dictionary<int, GlobalStructureInfo>();
            var newIndexByPlayfield = new Dictionary<string, HashSet<int>>();
            
            foreach (var structure in allStructures)
            {
                newStructures[structure.id] = structure;
                
                if (!newIndexByPlayfield.ContainsKey(structure.playfield))
                {
                    newIndexByPlayfield[structure.playfield] = new HashSet<int>();
                }
                newIndexByPlayfield[structure.playfield].Add(structure.id);
            }
            
            // === Детектирование изменений ===
            
            // Найти уничтоженные структуры
            var destroyedIds = _structures.Keys.Except(newStructures.Keys).ToList();
            foreach (var destroyedId in destroyedIds)
            {
                if (_structures.TryRemove(destroyedId, out var destroyedStructure))
                {
                    _logger.LogInformation(
                        $"Structure destroyed: {destroyedStructure.name} " +
                        $"(EntityId={destroyedId}, Playfield={destroyedStructure.playfield})"
                    );
                    
                    StructureDestroyed?.Invoke(this, new StructureEventArgs
                    {
                        Structure = destroyedStructure,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
            
            // Найти созданные структуры
            var createdIds = newStructures.Keys.Except(_structures.Keys).ToList();
            foreach (var createdId in createdIds)
            {
                var createdStructure = newStructures[createdId];
                
                _logger.LogInformation(
                    $"Structure created: {createdStructure.name} " +
                    $"(EntityId={createdId}, Playfield={createdStructure.playfield})"
                );
                
                StructureCreated?.Invoke(this, new StructureEventArgs
                {
                    Structure = createdStructure,
                    Timestamp = DateTime.UtcNow
                });
            }
            
            // Обновление кэша
            foreach (var kvp in newStructures)
            {
                _structures[kvp.Key] = kvp.Value;
            }
            
            // Обновление индекса по playfield
            foreach (var kvp in newIndexByPlayfield)
            {
                _structuresByPlayfield[kvp.Key] = kvp.Value;
            }
            
            _logger.LogDebug($"Structures refreshed: Total={allStructures.Count}, " +
                            $"Created={createdIds.Count}, Destroyed={destroyedIds.Count}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing structures");
        }
    }
    
    /// <summary>
    /// Получение структур на playfield
    /// </summary>
    public List<GlobalStructureInfo> GetStructuresOnPlayfield(
        string playfield, 
        int? factionId = null)
    {
        if (string.IsNullOrWhiteSpace(playfield))
        {
            return new List<GlobalStructureInfo>();
        }
        
        if (!_structuresByPlayfield.TryGetValue(playfield, out var structureIds))
        {
            return new List<GlobalStructureInfo>();
        }
        
        var structures = structureIds
            .Select(id => _structures.TryGetValue(id, out var s) ? s : null)
            .Where(s => s != null)
            .ToList();
        
        // Фильтрация по фракции
        if (factionId.HasValue)
        {
            structures = structures.Where(s => s.factionId == factionId.Value).ToList();
        }
        
        return structures;
    }
    
    /// <summary>
    /// Получение структуры по ID
    /// </summary>
    public GlobalStructureInfo GetStructure(int entityId)
    {
        if (_structures.TryGetValue(entityId, out var structure))
        {
            return structure;
        }
        
        return null;
    }
    
    /// <summary>
    /// Проверка существования
    /// </summary>
    public bool StructureExists(int entityId)
    {
        return _structures.ContainsKey(entityId);
    }
}
```

---

## 5. Использование в Colony Evolution

```csharp
public class ColonyEvolution
{
    private readonly IStructureTracker _structureTracker;
    
    public void Start()
    {
        // Подписка на разрушение структур
        _structureTracker.StructureDestroyed += OnStructureDestroyed;
    }
    
    private void OnStructureDestroyed(object sender, StructureEventArgs e)
    {
        // Проверяем, является ли это структура нашей колонии
        var colony = _state.Colonies.FirstOrDefault(
            c => c.MainStructureId == e.Structure.id
        );
        
        if (colony != null)
        {
            _logger.LogWarning($"Colony {colony.Id} base destroyed!");
            
            // Откат на предыдущую стадию
            await _stageManager.DowngradeColonyAsync(
                colony, 
                "Base destroyed by player"
            );
        }
    }
}
```

---

## 6. Тестирование

```csharp
[Fact]
public async Task RefreshStructuresAsync_DetectsNewStructures()
{
    // Arrange
    var gatewayMock = new Mock<IEmpyrionGateway>();
    var structures = new List<GlobalStructureInfo>
    {
        new GlobalStructureInfo { id = 1, name = "Base1" },
        new GlobalStructureInfo { id = 2, name = "Base2" }
    };
    
    gatewayMock
        .Setup(g => g.SendRequestAsync<List<GlobalStructureInfo>>(
            CmdId.Request_GlobalStructure_List,
            null,
            It.IsAny<int>()))
        .ReturnsAsync(structures);
    
    var tracker = new StructureTracker(gatewayMock.Object, _logger);
    
    bool eventFired = false;
    tracker.StructureCreated += (s, e) => { eventFired = true; };
    
    // Act
    await tracker.RefreshStructuresAsync();
    
    // Assert
    Assert.True(eventFired);
    Assert.True(tracker.StructureExists(1));
    Assert.True(tracker.StructureExists(2));
}
```

---

## 7. Чеклист разработчика

**Этап 1: Базовый трекинг (1 день)**
- [ ] Реализовать `IStructureTracker`
- [ ] Периодическое обновление через `RefreshStructuresAsync()`
- [ ] Кэширование структур
- [ ] Unit-тесты

**Этап 2: Детектирование изменений (1 день)**
- [ ] События `StructureCreated` / `StructureDestroyed`
- [ ] Оптимизация сравнения списков
- [ ] Integration тесты

---

## 8. Известные проблемы

### 8.1 Проблема: Ложные срабатывания "уничтожено"

**Причина:** При рестарте сервера все структуры исчезают из списка, затем появляются снова

**Решение:**
```csharp
// Не публиковать события сразу после старта
private DateTime _startTime = DateTime.UtcNow;

if ((DateTime.UtcNow - _startTime).TotalSeconds < 30)
{
    // Игнорировать изменения первые 30 секунд после старта
    return;
}
```

---

## 9. Связь с другими документами

- **[Module_07_Colony_Evolution.md](Module_07_Colony_Evolution.md)** — реагирует на разрушение структур
- **[Module_10_Threat_Director.md](Module_10_Threat_Director.md)** — отслеживает атаки на колонии

---

**Последнее обновление:** 28.01.2026
