# Модуль: Threat Director

**Приоритет разработки:** 2 (Высокий)  
**Зависимости:** Module_04 (Entity Spawner), Module_05 (AIM Orchestrator), Module_08 (Player Tracker), Module_12 (Hostility Tracker), Module_13 (Unit Economy)  
**Статус:** 🟡 В разработке

---

## 1. Назначение модуля

Threat Director управляет **уровнем угрозы колоний** и активирует защитные механизмы: спавнит охранников, запускает волны атак, эскалирует защиту при разрушениях.

### Ключевая механика

**Адаптивная защита** — чем больше игрок атакует колонию, тем сильнее она защищается.

---

## 2. Архитектурный контекст

```mermaid
graph TB
    CoreLoop[Core Loop]
    StructureTracker[Structure Tracker]
    PlayerTracker[Player Tracker]
    HostilityTracker[Hostility Tracker]
    
    ThreatDirector[Threat Director<br/>Этот модуль]
    
    EntitySpawner[Entity Spawner]
    AIMOrchestrator[AIM Orchestrator]
    EventBus[Event Bus]
    
    CoreLoop -->|Update tick| ThreatDirector
    StructureTracker -->|Structure Destroyed| ThreatDirector
    PlayerTracker -->|Player Near Colony| ThreatDirector
    HostilityTracker -->|Most Wanted Target| ThreatDirector
    HostilityTracker -->|Rank Changed| ThreatDirector
    
    ThreatDirector -->|Spawn guards| EntitySpawner
    ThreatDirector -->|Launch wave| AIMOrchestrator
    ThreatDirector -->|Publish events| EventBus
```

---

## 3. Уровни угрозы

### 3.1 Enum ThreatLevel

```csharp
public enum ThreatLevel
{
    None = 0,       // Нет угрозы (игроков нет на планете)
    Low = 1,        // Низкая (игроки далеко, >2км)
    Medium = 2,     // Средняя (игроки близко, <2км)
    High = 3,       // Высокая (игроки атакуют структуры)
    Critical = 4,   // Критическая (база разрушена)
    Hunt = 5        // ОХОТА: Most Wanted игрок онлайн (ранг 3+)
}
```

### 3.2 Реакции по уровням

| Уровень | Условия | Реакция |
|---------|---------|---------|
| **None** | Нет игроков на playfield | Деактивация патрулей |
| **Low** | Игроки >2км от базы | Базовые патрули (2-4 охранника) |
| **Medium** | Игроки 500м-2км от базы | Усиленные патрули (4-8 охранников) |
| **High** | Игроки атакуют (<500м) или урон по структурам | Волна атак (дроны) + подкрепление |
| **Critical** | База уничтожена | Массированная контратака + откат стадии |
| **Hunt** | Most Wanted онлайн (ранг 3+) | Специальные охотничьи отряды против конкретного игрока |

### 3.3 Интеграция с Most Wanted системой

**Приоритет угрозы:** Most Wanted > Proximity > Standard

```csharp
// Модификаторы уровня угрозы от ранга Most Wanted
var rankThreatModifier = mostWanted.Rank switch
{
    WantedRank.Offender => 1.25f,    // +25% патрулей
    WantedRank.Enemy => 1.5f,         // +50% патрулей + волны каждые 15 мин
    WantedRank.Terrorist => 2.0f,     // Охотничьи отряды каждые 10 мин
    WantedRank.Nemesis => 3.0f,       // Assassination Squad
    WantedRank.Genocider => 5.0f,     // CV-носитель + массированная охота
    _ => 1.0f
};
```

---

## 4. Интерфейс

```csharp
/// <summary>
/// Директор управления угрозами
/// </summary>
public interface IThreatDirector
{
    // === Анализ угрозы ===
    
    /// <summary>
    /// Расчет текущего уровня угрозы для колонии
    /// </summary>
    Task<ThreatLevel> CalculateThreatLevelAsync(Colony colony);
    
    /// <summary>
    /// Обновление уровня угрозы и активация защиты
    /// Вызывается в Core Loop для активных колоний
    /// </summary>
    Task UpdateThreatLevelAsync(Colony colony);
    
    // === Реакция на события ===
    
    /// <summary>
    /// Реакция на разрушение структуры колонии
    /// </summary>
    Task RespondToDestructionAsync(Colony colony, DestructionEvent destructionEvent);
    
    /// <summary>
    /// Реакция на атаку юнитов колонии
    /// </summary>
    Task RespondToUnitAttackAsync(Colony colony, int attackedUnitId);
    
    // === Активация защиты ===
    
    /// <summary>
    /// Активация защитных механизмов согласно уровню угрозы
    /// </summary>
    Task ActivateDefensesAsync(Colony colony, ThreatLevel level);
    
    /// <summary>
    /// Запуск волны атак на вражескую структуру (v1.15+)
    /// </summary>
    Task<uint> LaunchWaveAttackAsync(Colony colony, int targetEntityId, int waveStrength);
    
    /// <summary>
    /// Спавн(направление к месту инцидента) дополнительных защитников из резерва колонии.
    /// </summary>
    Task SpawnDefendersAsync(Colony colony, Vector3 spawnPosition, int count);
}
```

---

## 5. Модели данных

```csharp
/// <summary>
/// Событие разрушения структуры
/// </summary>
public class DestructionEvent
{
    public int EntityId { get; set; }
    public string EntityName { get; set; }
    public DateTime Timestamp { get; set; }
    public int? AttackerPlayerId { get; set; }
}

/// <summary>
/// Данные о волне атаки (v1.15+)
/// </summary>
public class WaveAttackData
{
    public string WaveName { get; set; }
    public string TargetEntityId { get; set; }
    public string Faction { get; set; } = "Zirax";
    public int Cost { get; set; }  // Сложность волны
}
```

---

## 6. Реализация (ключевые методы)

```csharp
public class ThreatDirector : IThreatDirector
{
    private readonly ConcurrentDictionary<string, List<DestructionEvent>> _attackHistory;
    private readonly ConcurrentDictionary<string, DateTime> _lastWaveTime;
    private const int MinWaveIntervalSeconds = 300;
    
    public async Task<ThreatLevel> CalculateThreatLevelAsync(Colony colony)
    {
        var score = 0f;
        
        // ПРИОРИТЕТ: Проверка Most Wanted системы
        var mostWanted = _hostilityTracker.GetMostWantedTarget(colony);
        if (mostWanted != null && mostWanted.IsOnline && mostWanted.Rank >= WantedRank.Terrorist)
        {
            // Most Wanted ранга 3+ онлайн - активируем режим охоты
            _logger.LogWarning(
                $"Colony {colony.Id} entering HUNT mode: Most Wanted player {mostWanted.PlayerId} " +
                $"online (rank: {mostWanted.Rank}, score: {mostWanted.HostilityScore})"
            );
            return ThreatLevel.Hunt;
        }
        
        // Фактор 1: Близость игроков
        var nearbyPlayers = _playerTracker.GetPlayersNearPosition(colony.Playfield, colony.Position, 2000f);
        if (!nearbyPlayers.Any()) return ThreatLevel.None;
        
        var veryClose = nearbyPlayers.Count(p => Vector3.Distance(p.Position, colony.Position) < 500f);
        score += veryClose * 20f + (nearbyPlayers.Count - veryClose) * 5f;
        
        // Фактор 2: Недавние разрушения
        if (_attackHistory.TryGetValue(colony.Id, out var attacks))
            score += attacks.Count(a => (DateTime.UtcNow - a.Timestamp).TotalMinutes < 30) * 15f;
        
        // Фактор 3: Затухание угрозы
        var timeSinceLastAttack = (DateTime.UtcNow - colony.LastAttackTime).TotalMinutes;
        if (timeSinceLastAttack < 30)
            score += (30 - timeSinceLastAttack) * 0.5f;
        
        // Фактор 4: Ценность колонии
        score += (int)colony.Stage * 3f;
        
        // Фактор 5: Модификатор от Most Wanted (даже если оффлайн)
        if (mostWanted != null)
        {
            var rankModifier = mostWanted.Rank switch
            {
                WantedRank.Offender => 1.25f,
                WantedRank.Enemy => 1.5f,
                WantedRank.Terrorist => 2.0f,
                WantedRank.Nemesis => 3.0f,
                WantedRank.Genocider => 5.0f,
                _ => 1.0f
            };
            score *= rankModifier;
        }
        
        // Преобразование в уровень
        if (score < 10) return ThreatLevel.None;
        if (score < 30) return ThreatLevel.Low;
        if (score < 60) return ThreatLevel.Medium;
        if (score < 100) return ThreatLevel.High;
        return ThreatLevel.Critical;
    }
    
    public async Task UpdateThreatLevelAsync(Colony colony)
    {
        var newLevel = await CalculateThreatLevelAsync(colony);
        if (newLevel != colony.ThreatLevel)
        {
            colony.ThreatLevel = newLevel;
            if (newLevel > colony.ThreatLevel)
                await ActivateDefensesAsync(colony, newLevel);
        }
    }
    
    public async Task RespondToDestructionAsync(Colony colony, DestructionEvent destructionEvent)
    {
        var attacks = _attackHistory.GetOrAdd(colony.Id, _ => new List<DestructionEvent>());
        attacks.Add(destructionEvent);
        
        colony.LastAttackTime = DateTime.UtcNow;
        colony.ThreatLevel = ThreatLevel.Critical;
        
        await ActivateDefensesAsync(colony, ThreatLevel.Critical);
    }
    
    public async Task ActivateDefensesAsync(Colony colony, ThreatLevel level)
    {
        switch (level)
        {
            case ThreatLevel.Medium:
                await SpawnDefendersAsync(colony, colony.Position, 4);
                break;
            
            case ThreatLevel.High:
                await SpawnDefendersAsync(colony, colony.Position, 6);
                var target = FindNearestPlayerStructure(colony);
                if (target != null) await LaunchWaveAttackAsync(colony, target.id, 50);
                break;
            
            case ThreatLevel.Critical:
                await SpawnDefendersAsync(colony, colony.Position, 10);
                target = FindNearestPlayerStructure(colony);
                if (target != null) await LaunchWaveAttackAsync(colony, target.id, 150);
                break;
            
            case ThreatLevel.Hunt:
                // РЕЖИМ ОХОТЫ: целенаправленная охота на Most Wanted игрока
                var mostWanted = _hostilityTracker.GetMostWantedTarget(colony);
                if (mostWanted != null && mostWanted.IsOnline)
                {
                    await LaunchHuntOperationAsync(colony, mostWanted);
                }
                break;
        }
    }
    
    /// <summary>
    /// Запуск операции охоты на конкретного игрока
    /// </summary>
    private async Task LaunchHuntOperationAsync(Colony colony, PlayerHostilityInfo target)
    {
        _logger.LogWarning(
            $"Launching HUNT operation against player {target.PlayerId} " +
            $"(rank: {target.Rank}, score: {target.HostilityScore})"
        );
        
        switch (target.Rank)
        {
            case WantedRank.Offender:
                // Усиленные патрули (+25%)
                await SpawnDefendersAsync(colony, colony.Position, 3);
                break;
            
            case WantedRank.Enemy:
                // Периодические волны каждые 15 минут
                await SpawnDefendersAsync(colony, colony.Position, 5);
                if (ShouldLaunchWave(colony, TimeSpan.FromMinutes(15)))
                {
                    var playerStructure = FindPlayerStructure(target.PlayerId);
                    if (playerStructure != null)
                        await LaunchWaveAttackAsync(colony, playerStructure.id, 75);
                }
                break;
            
            case WantedRank.Terrorist:
                // Элитные охотничьи отряды каждые 10 минут
                if (ShouldLaunchHunterSquad(colony, TimeSpan.FromMinutes(10)))
                {
                    await SpawnEliteHuntersAsync(colony, target, count: 7);
                }
                break;
            
            case WantedRank.Nemesis:
                // Assassination Squad - отряд убийц
                if (ShouldLaunchAssassinationSquad(colony, TimeSpan.FromMinutes(20)))
                {
                    await SpawnAssassinationSquadAsync(colony, target, count: 12);
                }
                // Бомбардировки баз игрока
                var playerBase = FindPlayerStructure(target.PlayerId);
                if (playerBase != null)
                    await LaunchWaveAttackAsync(colony, playerBase.id, 200);
                break;
            
            case WantedRank.Genocider:
                // CV-носитель + массированная непрерывная охота
                await SpawnCarrierCVAsync(colony);
                await SpawnAssassinationSquadAsync(colony, target, count: 15);
                await DeployForwardOperatingBaseAsync(colony, target);
                
                // Массированные атаки на все структуры игрока
                var allPlayerStructures = _structureTracker.GetPlayerStructures(target.PlayerId);
                foreach (var structure in allPlayerStructures.Take(3))
                {
                    await LaunchWaveAttackAsync(colony, structure.id, 300);
                }
                break;
        }
    }
    
    public async Task<uint> LaunchWaveAttackAsync(Colony colony, int targetEntityId, int waveStrength)
    {
        // Rate limit check
        if (_lastWaveTime.TryGetValue(colony.Id, out var lastTime))
        {
            if ((DateTime.UtcNow - lastTime).TotalSeconds < MinWaveIntervalSeconds)
                return 0;
        }
        
        var waveData = new WaveAttackData
        {
            WaveName = $"GLEX_Defense_{colony.Id}",
            TargetEntityId = targetEntityId.ToString(),
            Faction = "Zirax",
            Cost = waveStrength
        };
        
        var waveId = await _aimOrchestrator.CreateWaveAttackAsync(waveData);
        _lastWaveTime[colony.Id] = DateTime.UtcNow;
        return waveId;
    }
}
```

---

## 7. Использование в Core Loop

```csharp
public class SimulationEngine
{
    private readonly IThreatDirector _threatDirector;
    
    public void OnSimulationTick(object state)
    {
        foreach (var colony in _state.Colonies)
        {
            // Обновляем угрозу для активных колоний
            if (_playerTracker.HasPlayersOnPlayfield(colony.Playfield))
            {
                await _threatDirector.UpdateThreatLevelAsync(colony);
            }
        }
    }
}
```

---

## 8. Интеграция с Unit Economy Manager

**КРИТИЧЕСКИ ВАЖНО:** С версии 1.1 все спавны защитников проходят через Unit Economy для учета доступности юнитов.

### 8.1 Модифицированный SpawnDefendersAsync

```csharp
public async Task SpawnDefendersAsync(Colony colony, Vector3 position, int requestedCount)
{
    // ШАГ 1: Проверка доступности юнитов через Unit Economy
    if (!_unitEconomy.CanSpawnUnit(colony, UnitType.Guard, requestedCount))
    {
        var available = _unitEconomy.GetAvailableCount(colony, UnitType.Guard);
        
        _logger.LogWarning(
            $"Colony {colony.Id} cannot spawn {requestedCount} guards. " +
            $"Only {available} available. Resources depleted!"
        );
        
        // Спавним сколько можем
        requestedCount = Math.Min(requestedCount, available);
        
        if (requestedCount == 0)
        {
            _logger.LogError(
                $"Colony {colony.Id} has NO guards available! Cannot defend. " +
                $"Production rate: {colony.UnitPool.ProductionRate:F2}/hour"
            );
            
            // Отправляем сообщение игрокам (опционально)
            await NotifyPlayersAsync(colony.Playfield, 
                "[Zirax Command] Defenses critically weakened. Reinforcements delayed.");
            
            return;
        }
    }
    
    // ШАГ 2: Резервируем юниты (уменьшает доступное количество)
    if (!_unitEconomy.ReserveUnits(colony, UnitType.Guard, requestedCount))
    {
        _logger.LogError($"Failed to reserve {requestedCount} guards");
        return;
    }
    
    // ШАГ 3: Спавним через Entity Spawner
    var spawnedIds = await _entitySpawner.SpawnNPCGroupAsync(
        "ZiraxMinigunPatrol",
        position,
        requestedCount,
        colony.FactionId
    );
    
    // ШАГ 4: Регистрируем активные юниты в Unit Economy
    foreach (var entityId in spawnedIds)
    {
        _unitEconomy.RegisterActiveUnit(colony, entityId, UnitType.Guard, "Defender");
    }
    
    var remaining = _unitEconomy.GetAvailableCount(colony, UnitType.Guard);
    
    _logger.LogInformation(
        $"Colony {colony.Id} deployed {requestedCount} guards. " +
        $"Remaining in reserve: {remaining}/{colony.UnitPool.MaxGuards}"
    );
}
```

### 8.2 Обработка исчерпания резервов

```csharp
private async Task HandleDepletedReserves(Colony colony, ThreatLevel level)
{
    var pool = _unitEconomy.GetUnitPool(colony);
    
    if (pool.AvailableGuards == 0 && pool.AvailableDrones == 0)
    {
        // Критическая ситуация: нет юнитов для защиты
        _logger.LogCritical(
            $"Colony {colony.Id} completely out of units! " +
            $"Production rate: {pool.ProductionRate:F2}/hour. " +
            $"Next unit in {CalculateTimeToNextUnit(pool)} minutes."
        );
        
        // Временно снижаем уровень угрозы (база не может ответить)
        colony.ThreatLevel = ThreatLevel.Low;
        
        // Уведомляем игрока о временном преимуществе
        await NotifyPlayersAsync(
            colony.Playfield,
            "[Zirax Command] Our forces are stretched thin. This is your chance."
        );
    }
}

private float CalculateTimeToNextUnit(UnitPool pool)
{
    if (pool.ProductionRate <= 0) return float.MaxValue;
    
    var remainingProgress = 1.0f - pool.ProductionProgress;
    var hoursNeeded = remainingProgress / pool.ProductionRate;
    return hoursNeeded * 60f; // В минутах
}
```

### 8.3 Адаптация к нехватке ресурсов

```csharp
public async Task ActivateDefensesAsync(Colony colony, ThreatLevel level)
{
    var pool = _unitEconomy.GetUnitPool(colony);
    
    switch (level)
    {
        case ThreatLevel.Medium:
            // Пытаемся вызвать 4 охранника
            var mediumCount = Math.Min(4, pool.AvailableGuards);
            if (mediumCount > 0)
                await SpawnDefendersAsync(colony, colony.Position, mediumCount);
            break;
        
        case ThreatLevel.High:
            // Пытаемся вызвать 6 охранников + волну дронов
            var highCount = Math.Min(6, pool.AvailableGuards);
            if (highCount > 0)
                await SpawnDefendersAsync(colony, colony.Position, highCount);
            
            // Волна дронов (если есть доступные)
            if (pool.AvailableDrones >= 5)
            {
                var target = FindNearestPlayerStructure(colony);
                if (target != null)
                {
                    var droneCost = 5; // Дроны на волну
                    if (_unitEconomy.ReserveUnits(colony, UnitType.Drone, droneCost))
                        await LaunchWaveAttackAsync(colony, target.id, 50);
                }
            }
            break;
        
        case ThreatLevel.Critical:
            // Выделяем ВСЕ доступные резервы
            if (pool.AvailableGuards > 0)
                await SpawnDefendersAsync(colony, colony.Position, pool.AvailableGuards);
            
            if (pool.AvailableDrones >= 10)
            {
                var target = FindNearestPlayerStructure(colony);
                if (target != null && _unitEconomy.ReserveUnits(colony, UnitType.Drone, 10))
                    await LaunchWaveAttackAsync(colony, target.id, 150);
            }
            break;
    }
}
```

---

## 9. Тестирование

```csharp
[Fact]
public async Task CalculateThreatLevel_NoPlayers_ReturnsNone()
{
    // Arrange
    var colony = CreateTestColony();
    var playerTrackerMock = new Mock<IPlayerTracker>();
    playerTrackerMock
        .Setup(p => p.GetPlayersNearPosition(It.IsAny<string>(), It.IsAny<Vector3>(), It.IsAny<float>()))
        .Returns(new List<PlayerInfo>());
    
    var director = new ThreatDirector(
        _spawner,
        _aim,
        playerTrackerMock.Object,
        _logger
    );
    
    // Act
    var level = await director.CalculateThreatLevelAsync(colony);
    
    // Assert
    Assert.Equal(ThreatLevel.None, level);
}

[Fact]
public async Task RespondToDestruction_EscalatesToCritical()
{
    // Arrange
    var colony = CreateTestColony();
    colony.ThreatLevel = ThreatLevel.Low;
    
    var director = CreateThreatDirector();
    
    // Act
    await director.RespondToDestructionAsync(colony, new DestructionEvent
    {
        EntityId = 123,
        EntityName = "TestBase",
        Timestamp = DateTime.UtcNow
    });
    
    // Assert
    Assert.Equal(ThreatLevel.Critical, colony.ThreatLevel);
}
```

---

## 9. Чеклист разработчика

**Этап 1: Расчет угрозы (2 дня)**
- [ ] Реализовать `CalculateThreatLevelAsync()`
- [ ] Факторы: игроки, разрушения, время
- [ ] Unit-тесты для всех уровней

**Этап 2: Активация защиты (2 дня)**
- [ ] Реализовать `ActivateDefensesAsync()`
- [ ] Спавн защитников
- [ ] Интеграция с EntitySpawner

**Этап 3: Волны атак (1 день)**
- [ ] Реализовать `LaunchWaveAttackAsync()` с IPda API
- [ ] Rate limiting
- [ ] Тесты на реальном сервере

**Этап 4: Реакция на события (1 день)**
- [ ] `RespondToDestructionAsync()`
- [ ] `RespondToUnitAttackAsync()`
- [ ] Интеграция со StructureTracker

---

## 10. Известные проблемы

### 10.1 Проблема: Спам волн атак

**Решение:** Rate limiting (минимум 5 минут между волнами)

### 10.2 Проблема: NPC не атакуют после спавна

**Решение:** Использовать IPda.CreateWaveAttack() вместо простого спавна (v1.15+)

---

## 11. Связь с другими документами

- **[Module_04_Entity_Spawner.md](Module_04_Entity_Spawner.md)** — спавн защитников и охотников
- **[Module_05_AIM_Orchestrator.md](Module_05_AIM_Orchestrator.md)** — волны атак
- **[Module_08_Player_Tracker.md](Module_08_Player_Tracker.md)** — близость игроков и онлайн статус
- **[Module_12_Hostility_Tracker.md](Module_12_Hostility_Tracker.md)** — система Most Wanted и ранги враждебности
- **[Module_13_Unit_Economy.md](Module_13_Unit_Economy.md)** — управление доступностью юнитов и производством ✨ НОВОЕ

---

**Последнее обновление:** 29.01.2026
