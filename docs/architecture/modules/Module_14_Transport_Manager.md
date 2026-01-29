# Модуль: Transport Manager

**Приоритет разработки:** 3 (Средний - улучшение иммерсивности)  
**Зависимости:** Module_04 (Entity Spawner), Module_05 (AIM Orchestrator), Module_13 (Unit Economy)  
**Статус:** 🔴 Не начат

---

## 1. Назначение модуля

Transport Manager управляет **транспортировкой юнитов в пределах планеты** для повышения иммерсивности. Вместо мгновенного спавна юниты прибывают на транспорте, создавая ощущение живой военной инфраструктуры.

### Ключевые принципы

**Локальная логика:**
- ✅ Транспорт работает только внутри одной планеты (playfield)
- ✅ Межсистемные перелеты НЕ реализуются (слишком долго)
- ✅ Используются существующие структуры колонии (база, портал)

---

## 2. Сценарии использования

### 2.1 Атака на аванпост → десант с базы

```
[Аванпост атакован] → [База отправляет DropShip] → [DropShip доставляет охранников]
```

**Логика:**
- Игрок атакует ресурсный аванпост
- Главная база колонии отправляет десантный корабль
- DropShip летит от базы к аванпосту (1-2 минуты)
- Охранники высаживаются рядом с аванпостом

### 2.2 Атака на единственную базу → спавн из базы/портала

```
[База атакована] → [Нет других баз] → [Защитники выходят из базы ИЛИ портала]
```

**Логика:**
- Игрок атакует главную базу колонии
- Если есть портал → охранники спавнятся у портала (телепорт подкрепления)
- Если нет портала → охранники спавнятся у выхода из базы (гарнизон)
- **НЕТ транспорта** (некуда вызывать, база сама под атакой)

### 2.3 Патрули → запуск из ангара базы

```
[Плановое патрулирование] → [SV вылетает из ангара] → [Патруль по маршруту]
```

**Логика:**
- База запускает патрульные корабли
- SV спавнится рядом с базой (имитация ангара)
- SV поднимается и начинает патруль

### 2.4 Логистика → снабжение аванпостов

```
[Регулярный рейс] → [HV/SV едет к аванпосту] → [Возвращается к базе]
```

**Логика (визуальный эффект):**
- База отправляет логистический транспорт к аванпосту
- Транспорт движется, останавливается, возвращается
- Не влияет на геймплей, только визуал

---

## 3. Интерфейс

```csharp
/// <summary>
/// Управление транспортировкой юнитов внутри планеты
/// </summary>
public interface ITransportManager
{
    // === Основные операции ===
    
    /// <summary>
    /// Доставка защитников к атакованной структуре.
    /// Если destination == главная база → спавн из базы/портала.
    /// Если destination != главная база → транспорт от базы.
    /// </summary>
    Task<TransportMission> DeployDefendersAsync(
        Colony colony,
        Vector3 destination,
        int defenderCount
    );
    
    /// <summary>
    /// Запуск патрульного корабля из ангара базы
    /// </summary>
    Task<int> LaunchPatrolVesselAsync(
        Colony colony,
        List<Vector3> patrolRoute
    );
    
    /// <summary>
    /// Отправка логистического транспорта (визуальный эффект)
    /// </summary>
    Task StartLogisticsRunAsync(
        Colony colony,
        Vector3 outpostPosition
    );
    
    // === Управление миссиями ===
    
    /// <summary>
    /// Обновление активных транспортных миссий (вызывается в Core Loop)
    /// </summary>
    void UpdateTransportMissions(float deltaTime);
    
    /// <summary>
    /// Получение списка активных миссий колонии
    /// </summary>
    List<TransportMission> GetActiveMissions(string colonyId);
}

/// <summary>
/// Транспортная миссия
/// </summary>
public class TransportMission
{
    public string MissionId { get; set; }
    public string ColonyId { get; set; }
    public int TransportEntityId { get; set; }
    public TransportType TransportType { get; set; }
    
    public Vector3 StartPosition { get; set; }
    public Vector3 Destination { get; set; }
    public MissionPhase CurrentPhase { get; set; }
    
    public int CargoCount { get; set; }
    public List<int> DeployedUnitIds { get; set; } = new();
    
    public DateTime StartTime { get; set; }
    public DateTime? EstimatedArrival { get; set; }
    
    public bool IsDirectSpawn { get; set; }  // true = спавн без транспорта (база/портал)
}

public enum TransportType
{
    None,           // Прямой спавн (без транспорта)
    DropShip,       // Десантный корабль (SV)
    PatrolSV,       // Патрульный корабль
    LogisticsHV,    // Наземный транспорт
    LogisticsSV     // Воздушный транспорт
}

public enum MissionPhase
{
    Preparing,      // Подготовка (спавн транспорта)
    EnRoute,        // В пути
    Deploying,      // Высадка
    Returning,      // Возвращение
    Completed       // Завершена
}
```

---

## 4. Реализация (ключевые методы)

### 4.1 Умная доставка защитников

```csharp
/// <summary>
/// Доставка защитников с автоматическим выбором метода:
/// - Атака на главную базу → спавн из базы/портала
/// - Атака на аванпост → транспорт от базы
/// </summary>
public async Task<TransportMission> DeployDefendersAsync(
    Colony colony,
    Vector3 destination,
    int defenderCount)
{
    // ШАГ 1: Проверка и резервирование юнитов
    if (!_unitEconomy.CanSpawnUnit(colony, UnitType.Guard, defenderCount))
    {
        var available = _unitEconomy.GetAvailableCount(colony, UnitType.Guard);
        defenderCount = Math.Min(defenderCount, available);
        if (defenderCount == 0) return null;
    }
    
    if (!_unitEconomy.ReserveUnits(colony, UnitType.Guard, defenderCount))
        return null;
    
    // ШАГ 2: Определяем, защита главной базы или аванпоста
    var distanceToBase = Vector3.Distance(destination, colony.Position);
    var isMainBaseDefense = distanceToBase < 200f;  // Атака в радиусе 200м от базы
    
    if (isMainBaseDefense)
    {
        // СЦЕНАРИЙ A: Защита главной базы → спавн локально
        return await DeployFromBaseOrPortalAsync(colony, destination, defenderCount);
    }
    else
    {
        // СЦЕНАРИЙ B: Защита аванпоста → транспорт от базы
        return await DeployViaTransportAsync(colony, destination, defenderCount);
    }
}

/// <summary>
/// СЦЕНАРИЙ A: Спавн защитников из базы или портала (без транспорта)
/// </summary>
private async Task<TransportMission> DeployFromBaseOrPortalAsync(
    Colony colony,
    Vector3 destination,
    int defenderCount)
{
    Vector3 spawnPosition;
    string spawnSource;
    
    // Приоритет: портал → база
    if (colony.HasPortal && colony.PortalPosition != null)
    {
        spawnPosition = colony.PortalPosition.Value;
        spawnSource = "Portal";
        _logger.LogInformation(
            $"Colony {colony.Id}: Deploying {defenderCount} defenders via Portal"
        );
    }
    else
    {
        // Спавн рядом с базой (имитация выхода из ангара/казармы)
        spawnPosition = colony.Position + new Vector3(
            Random.Range(-20, 20),
            0,
            Random.Range(-20, 20)
        );
        spawnSource = "Base Garrison";
        _logger.LogInformation(
            $"Colony {colony.Id}: Deploying {defenderCount} defenders from Base Garrison"
        );
    }
    
    // Мгновенный спавн защитников
    var guardIds = await _spawner.SpawnNPCGroupAsync(
        "ZiraxMinigunPatrol",
        spawnPosition,
        defenderCount,
        colony.FactionId
    );
    
    // Регистрируем активные юниты
    foreach (var entityId in guardIds)
    {
        _unitEconomy.RegisterActiveUnit(colony, entityId, UnitType.Guard, spawnSource);
    }
    
    // Создаем миссию (для статистики, без транспорта)
    var mission = new TransportMission
    {
        MissionId = Guid.NewGuid().ToString(),
        ColonyId = colony.Id,
        TransportType = TransportType.None,
        StartPosition = spawnPosition,
        Destination = destination,
        CurrentPhase = MissionPhase.Completed,
        CargoCount = defenderCount,
        DeployedUnitIds = guardIds,
        StartTime = DateTime.UtcNow,
        IsDirectSpawn = true
    };
    
    return mission;
}

/// <summary>
/// СЦЕНАРИЙ B: Доставка защитников через транспорт
/// </summary>
private async Task<TransportMission> DeployViaTransportAsync(
    Colony colony,
    Vector3 destination,
    int defenderCount)
{
    // Спавн транспорта над базой
    var spawnPosition = new Vector3(
        colony.Position.X + Random.Range(-30, 30),
        colony.Position.Y + 250f,  // Высоко в небе
        colony.Position.Z + Random.Range(-30, 30)
    );
    
    var transportId = await _spawner.SpawnStructureAsync(
        "GLEX_DropShip_T1",
        spawnPosition,
        Vector3.Zero,
        colony.FactionId
    );
    
    var travelTime = CalculateTravelTime(spawnPosition, destination);
    
    var mission = new TransportMission
    {
        MissionId = Guid.NewGuid().ToString(),
        ColonyId = colony.Id,
        TransportEntityId = transportId,
        TransportType = TransportType.DropShip,
        StartPosition = spawnPosition,
        Destination = destination,
        CurrentPhase = MissionPhase.EnRoute,
        CargoCount = defenderCount,
        StartTime = DateTime.UtcNow,
        EstimatedArrival = DateTime.UtcNow.AddSeconds(travelTime),
        IsDirectSpawn = false
    };
    
    _activeMissions[mission.MissionId] = mission;
    
    _logger.LogInformation(
        $"Transport mission {mission.MissionId}: DropShip dispatched with {defenderCount} guards, ETA: {travelTime:F0}s"
    );
    
    // Запускаем движение транспорта
    await InitiateTransportMovementAsync(mission);
    
    return mission;
}
```

### 4.2 Обновление миссий

```csharp
/// <summary>
/// Обновление всех активных транспортных миссий
/// </summary>
public void UpdateTransportMissions(float deltaTime)
{
    foreach (var mission in _activeMissions.Values.ToList())
    {
        UpdateMissionAsync(mission, deltaTime).Wait();
    }
}

/// <summary>
/// Обновление отдельной миссии
/// </summary>
private async Task UpdateMissionAsync(TransportMission mission, float deltaTime)
{
    if (mission.CurrentPhase == MissionPhase.Completed)
        return;
    
    // Проверяем, существует ли транспорт
    if (!await _spawner.EntityExistsAsync(mission.TransportEntityId))
    {
        _logger.LogWarning($"Mission {mission.MissionId}: Transport destroyed!");
        _activeMissions.TryRemove(mission.MissionId, out _);
        return;
    }
    
    var transportEntity = _playfield.Entities[mission.TransportEntityId];
    
    switch (mission.CurrentPhase)
    {
        case MissionPhase.EnRoute:
            var distanceToTarget = Vector3.Distance(transportEntity.Pos, mission.Destination);
            if (distanceToTarget < 80f)  // Прибыли в зону высадки
            {
                mission.CurrentPhase = MissionPhase.Deploying;
                await DeployCargoAsync(mission);
            }
            break;
        
        case MissionPhase.Deploying:
            // Пауза после высадки
            if ((DateTime.UtcNow - mission.StartTime).TotalSeconds > 5)
            {
                mission.CurrentPhase = MissionPhase.Returning;
                await InitiateReturnAsync(mission);
            }
            break;
        
        case MissionPhase.Returning:
            var distanceToBase = Vector3.Distance(transportEntity.Pos, mission.StartPosition);
            if (distanceToBase < 100f)
            {
                await CompleteMissionAsync(mission);
            }
            break;
    }
}

/// <summary>
/// Высадка груза из транспорта
/// </summary>
private async Task DeployCargoAsync(TransportMission mission)
{
    var colony = _state.Colonies.First(c => c.Id == mission.ColonyId);
    var transportEntity = _playfield.Entities[mission.TransportEntityId];
    
    // Останавливаем транспорт
    _aimOrchestrator.StopNPC(transportEntity);
    
    // Высадка юнитов под транспортом
    var deployPosition = new Vector3(
        mission.Destination.X,
        mission.Destination.Y - 40f,
        mission.Destination.Z
    );
    
    var guardIds = await _spawner.SpawnNPCGroupAsync(
        "ZiraxMinigunPatrol",
        deployPosition,
        mission.CargoCount,
        colony.FactionId
    );
    
    // Регистрируем юниты
    foreach (var entityId in guardIds)
    {
        _unitEconomy.RegisterActiveUnit(colony, entityId, UnitType.Guard, "AirDrop");
        mission.DeployedUnitIds.Add(entityId);
    }
    
    _logger.LogInformation(
        $"Mission {mission.MissionId}: Deployed {guardIds.Count} guards at {mission.Destination}"
    );
}

/// <summary>
/// Завершение миссии
/// </summary>
private async Task CompleteMissionAsync(TransportMission mission)
{
    await _spawner.DestroyEntityAsync(mission.TransportEntityId);
    mission.CurrentPhase = MissionPhase.Completed;
    _activeMissions.TryRemove(mission.MissionId, out _);
    
    _logger.LogInformation($"Mission {mission.MissionId}: Completed successfully");
}
```

---

## 5. Интеграция с Threat Director

```csharp
// В ThreatDirector.SpawnDefendersAsync()
public async Task SpawnDefendersAsync(Colony colony, Vector3 incidentLocation, int count)
{
    // СТАРЫЙ КОД (прямой спавн):
    // var guardIds = await _spawner.SpawnNPCGroupAsync(...);
    
    // НОВЫЙ КОД (через Transport Manager):
    var mission = await _transportManager.DeployDefendersAsync(
        colony,
        incidentLocation,
        count
    );
    
    if (mission != null && !mission.IsDirectSpawn)
    {
        _logger.LogInformation(
            $"Reinforcements dispatched via {mission.TransportType}, " +
            $"ETA: {mission.EstimatedArrival?.ToString("HH:mm:ss")}"
        );
    }
    else if (mission != null && mission.IsDirectSpawn)
    {
        _logger.LogInformation($"Defenders deployed directly (base defense)");
    }
}
```

---

## 6. Конфигурация

```json
{
  "Transport": {
    "Enabled": true,
    "UseForOutpostDefense": true,
    "UseForPatrols": true,
    "UseForLogistics": false,
    "DropShipSpeed": 45.0,
    "DeploymentAltitude": 40.0,
    "BaseDefenseRadius": 200.0,
    "RadioMessages": {
      "ReinforcementsDispatched": "[Zirax Base] Reinforcements en route.",
      "ReinforcementsArrived": "[Zirax Base] Reinforcements deployed."
    }
  }
}
```

---

## 7. Игровой опыт

### До внедрения Transport Manager:
- ❌ Атакую аванпост → охранники телепортируются мгновенно
- ❌ Нет ощущения связи между базой и аванпостами

### После внедрения Transport Manager:
- ✅ Атакую аванпост → вижу десантный корабль, летящий от базы
- ✅ Могу перехватить транспорт → тактическая глубина
- ✅ База чувствуется как центр военной инфраструктуры
- ✅ Атака на саму базу → защитники выходят из портала/ангара (логично)

---

## 8. Чеклист разработчика

**Этап 1: Базовая структура (1 день)**
- [ ] Интерфейс `ITransportManager`
- [ ] Модели данных (`TransportMission`, enums)
- [ ] Регистрация в DI-контейнере

**Этап 2: Умная доставка (2 дня)**
- [ ] `DeployDefendersAsync()` с автовыбором метода
- [ ] `DeployFromBaseOrPortalAsync()` - прямой спавн
- [ ] `DeployViaTransportAsync()` - транспорт

**Этап 3: Обновление миссий (1 день)**
- [ ] `UpdateTransportMissions()`
- [ ] State machine для фаз миссии
- [ ] `DeployCargoAsync()`, `CompleteMissionAsync()`

**Этап 4: Интеграция (1 день)**
- [ ] Threat Director → Transport Manager
- [ ] Core Loop → `UpdateTransportMissions()`
- [ ] Конфигурация и тестирование

**Итого:** ~5 дней разработки

---

## 9. Известные ограничения

1. **Движение транспорта:** Используется простое прямое движение через IEntity API. Нет обхода препятствий.
2. **Межсистемные перелеты:** НЕ поддерживаются (слишком долго).
3. **Визуальные эффекты:** Нет партиклов при высадке (ограничение ModAPI).

---

## 10. Связь с другими документами

- **[Module_10_Threat_Director.md](Module_10_Threat_Director.md)** — основной потребитель Transport Manager
- **[Module_04_Entity_Spawner.md](Module_04_Entity_Spawner.md)** — спавн транспорта и юнитов
- **[Module_13_Unit_Economy.md](Module_13_Unit_Economy.md)** — резервирование юнитов перед доставкой
- **[Module_07_Colony_Evolution.md](Module_07_Colony_Evolution.md)** — порталы для телепортации

---

**Последнее обновление:** 29.01.2026  
**Размер:** ~470 строк
