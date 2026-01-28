# Стратегия тестирования GalacticExpansion (GLEX)

**Версия:** 1.0  
**Дата:** 24.01.2026  
**Статус:** Утверждено

---

## 1. Цели и принципы тестирования

### 1.1 Цели

1. **Обеспечить качество:** Выявить и устранить дефекты до релиза
2. **Подтвердить функциональность:** Все FR из ТЗ реализованы корректно
3. **Гарантировать надежность:** Мод работает стабильно длительное время
4. **Проверить производительность:** Нет деградации performance при нагрузке
5. **Обеспечить безопасность:** Нет уязвимостей и abuse-кейсов

### 1.2 Принципы

- **Test-Driven Development (TDD):** Тесты пишутся до или вместе с кодом
- **Автоматизация:** Все тесты автоматизированы и запускаются в CI
- **Изоляция:** Тесты независимы друг от друга
- **Репродуцируемость:** Тесты дают одинаковый результат при повторных запусках
- **Понятность:** Тесты служат документацией к коду

---

## 2. Уровни тестирования

### 2.1 Unit Tests — Модульное тестирование

**Цель:** Тестирование отдельных классов и методов в изоляции

**Охват:** > 70% code coverage для критичных модулей

**Инструменты:**
- **xUnit** — фреймворк тестирования
- **Moq** — мокирование зависимостей
- **FluentAssertions** — читаемые assertions

**Пример:**

```csharp
public class SimulationEngineTests
{
    [Fact]
    public void Start_LoadsStateFromStore()
    {
        // Arrange
        var mockStateStore = new Mock<IStateStore>();
        var testState = new SimulationState { Version = 1 };
        mockStateStore.Setup(s => s.LoadAsync()).ReturnsAsync(testState);
        
        var engine = new SimulationEngine(
            Mock.Of<ILogger<SimulationEngine>>(),
            Mock.Of<IModuleRegistry>(),
            Mock.Of<IEventBus>(),
            mockStateStore.Object
        );
        
        // Act
        await engine.StartAsync();
        
        // Assert
        mockStateStore.Verify(s => s.LoadAsync(), Times.Once);
    }
    
    [Theory]
    [InlineData(1000, 1.0f)] // 1 секунда
    [InlineData(2000, 2.0f)] // 2 секунды
    public void SimulationContext_CalculatesDeltaTimeCorrectly(int intervalMs, float expectedDt)
    {
        // Test logic
    }
}
```

**Тестируемые модули:**
- SimulationEngine
- StateStore
- RateLimiter
- SequenceManager
- PlacementResolver algorithms
- Economy calculations
- Threat level calculations

---

### 2.2 Integration Tests — Интеграционное тестирование

**Цель:** Тестирование взаимодействия между модулями

**Охват:** Все критичные интеграционные точки

**Подход:** Тестирование с реальными зависимостями (где возможно) или моками ModAPI

**Пример:**

```csharp
public class ColonyLifecycleIntegrationTests
{
    [Fact]
    public async Task Colony_ProgressesThroughStages()
    {
        // Arrange
        var testContainer = new TestContainer();
        var simulation = testContainer.GetService<ISimulationEngine>();
        var stateStore = testContainer.GetService<IStateStore>();
        
        // Создать тестовое состояние
        var initialState = new SimulationState
        {
            Version = 1,
            Colonies = new List<Colony>
            {
                new Colony
                {
                    Id = "test_colony",
                    Stage = ColonyStage.ConstructionYard,
                    Resources = new Resources { VirtualResources = 0, ProductionRate = 1000 }
                }
            }
        };
        
        await stateStore.SaveAsync(initialState);
        await simulation.StartAsync();
        
        // Act: Симуляция 2000 секунд (ресурсов накопится 2000)
        for (int i = 0; i < 2000; i++)
        {
            await Task.Delay(10); // Ускоренная симуляция
            simulation.Tick(1.0f);
        }
        
        // Assert
        var finalState = await stateStore.LoadAsync();
        var colony = finalState.Colonies.First();
        
        Assert.Equal(ColonyStage.BaseL1, colony.Stage); // Должна перейти в BaseL1
    }
}
```

**Тестируемые сценарии:**
- Полный lifecycle колонии (LandingPending → BaseMax)
- Спавн и удаление сущностей
- Детектирование разрушений
- Реакция на действия игроков
- Сохранение и загрузка состояния

---

### 2.3 E2E Tests — End-to-End тестирование

**Цель:** Тестирование системы в условиях, максимально близких к production

**Подход:** Запуск на test dedicated server с реальной игрой

**Инструменты:**
- Dedicated Server в контейнере
- Selenium/PlayWright для автоматизации клиента (опционально)
- Custom test harness для запуска сценариев

**Сценарии:**

1. **Full Colony Lifecycle:**
   - Запуск сервера с модом
   - Игрок входит на Akua
   - Проверка спавна DropShip
   - Проверка создания ConstructionYard
   - Ожидание перехода в BaseL1
   - Проверка наличия структуры в игре

2. **Player Destruction Response:**
   - Игрок разрушает базу
   - Проверка детектирования разрушения
   - Проверка усиления обороны
   - Проверка отката стадии

3. **Server Restart Persistence:**
   - Создание колоний
   - Остановка сервера
   - Запуск сервера
   - Проверка восстановления состояния

---

## 3. Инструменты

### 3.1 Test Framework

**xUnit 2.6+**

```xml
<PackageReference Include="xUnit" Version="2.6.0" />
<PackageReference Include="xUnit.runner.visualstudio" Version="2.6.0" />
```

**Структура тестового проекта:**

```
tests/
├── GalacticExpansion.Tests.Unit/
│   ├── Core/
│   │   ├── SimulationEngineTests.cs
│   │   └── EventBusTests.cs
│   ├── Gateway/
│   │   ├── EmpyrionGatewayTests.cs
│   │   └── RateLimiterTests.cs
│   └── ...
├── GalacticExpansion.Tests.Integration/
│   ├── ColonyLifecycleTests.cs
│   ├── SpawningTests.cs
│   └── StatePersis tenceTests.cs
└── GalacticExpansion.Tests.E2E/
    ├── FullScenarioTests.cs
    └── LoadTests.cs
```

### 3.2 Mocking

**Moq 4.20+**

```csharp
// Мокирование Gateway
var mockGateway = new Mock<IEmpyrionGateway>();
mockGateway
    .Setup(g => g.SendRequestAsync<int>(
        CmdId.Request_Entity_Spawn,
        It.IsAny<EntitySpawnInfo>(),
        It.IsAny<int>()))
    .ReturnsAsync(12345); // Entity ID

// Мокирование событий
mockGateway.Setup(g => g.GameEventReceived += It.IsAny<EventHandler<GameEventArgs>>());
```

### 3.3 Assertions

**FluentAssertions**

```csharp
colony.Stage.Should().Be(ColonyStage.BaseL1);
colony.Resources.VirtualResources.Should().BeGreaterThan(1000);
state.Colonies.Should().HaveCount(1);
```

---

## 4. Тестирование симуляции

### 4.1 Time-based Tests

**Проблема:** Симуляция зависит от времени

**Решение:** Мокирование времени

```csharp
public interface ITimeProvider
{
    DateTime UtcNow { get; }
}

// В продакшн:
public class SystemTimeProvider : ITimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}

// В тестах:
public class MockTimeProvider : ITimeProvider
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
    
    public void Advance(TimeSpan duration)
    {
        UtcNow += duration;
    }
}

// Использование в тесте:
var mockTime = new MockTimeProvider();
mockTime.Advance(TimeSpan.FromHours(1)); // "Перемотка" времени
```

### 4.2 Deterministic Tests

**Проблема:** Случайность в алгоритмах (например, размещение)

**Решение:** Seeded Random

```csharp
public class PlacementResolver
{
    private readonly Random _random;
    
    public PlacementResolver(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }
}

// В тесте:
var placement = new PlacementResolver(seed: 42); // Детерминированное поведение
```

---

## 5. Performance и нагрузочное тестирование

### 5.1 Benchmarking

**BenchmarkDotNet**

```csharp
[MemoryDiagnoser]
public class SimulationBenchmarks
{
    private SimulationEngine _engine;
    
    [GlobalSetup]
    public void Setup()
    {
        // Initialize
    }
    
    [Benchmark]
    public void SimulationTick_100Colonies()
    {
        var context = new SimulationContext
        {
            DeltaTime = 1.0f,
            CurrentState = CreateStateWith100Colonies()
        };
        
        _engine.Tick(context);
    }
}
```

**Метрики:**
- Время выполнения тика (должно быть < 100ms)
- Потребление памяти (должно быть < 50 MB)
- GC коллекции (минимум Gen2 коллекций)

### 5.2 Load Tests

**Сценарий:** 100 колоний, 50 игроков

```csharp
[Fact]
public async Task LoadTest_100Colonies_50Players()
{
    // Setup
    var state = CreateStateWithNColonies(100);
    var players = CreateNPlayers(50);
    
    // Warm-up
    for (int i = 0; i < 60; i++)
    {
        simulation.Tick(1.0f);
    }
    
    // Measure
    var stopwatch = Stopwatch.StartNew();
    for (int i = 0; i < 1000; i++)
    {
        simulation.Tick(1.0f);
    }
    stopwatch.Stop();
    
    // Assert
    var avgTickTime = stopwatch.ElapsedMilliseconds / 1000.0;
    Assert.True(avgTickTime < 100, $"Average tick time {avgTickTime}ms exceeds 100ms");
}
```

---

## 6. CI/CD стратегия

### 6.1 CI Pipeline

```yaml
# .github/workflows/ci.yml (пример для GitHub Actions)
name: CI

on: [push, pull_request]

jobs:
  test:
    runs-on: windows-latest
    
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '4.8'
      
      - name: Restore dependencies
        run: dotnet restore
      
      - name: Build
        run: dotnet build --no-restore
      
      - name: Run Unit Tests
        run: dotnet test tests/GalacticExpansion.Tests.Unit --no-build --verbosity normal
      
      - name: Run Integration Tests
        run: dotnet test tests/GalacticExpansion.Tests.Integration --no-build --verbosity normal
      
      - name: Generate Coverage Report
        run: dotnet test --collect:"XPlat Code Coverage"
      
      - name: Upload Coverage
        uses: codecov/codecov-action@v3
```

### 6.2 Автоматизация

**При каждом коммите:**
- Запуск всех Unit Tests
- Запуск Integration Tests
- Code coverage report
- Static code analysis

**При каждом Pull Request:**
- Все вышеперечисленное +
- Performance benchmarks
- Security scanning

**Перед релизом:**
- E2E Tests на test сервере
- Load Tests
- Manual QA

---

## 7. Test Coverage Goals

| Компонент | Целевой Coverage | Приоритет |
|-----------|------------------|-----------|
| Core Loop | 80% | Критический |
| Gateway | 75% | Критический |
| StateStore | 85% | Критический |
| SpawnEvo | 70% | Высокий |
| AIM Orchestrator | 80% | Высокий |
| ThreatDirector | 65% | Средний |
| EconomySim | 60% | Средний |
| PlacementResolver | 70% | Средний |