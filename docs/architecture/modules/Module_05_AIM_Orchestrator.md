# Модуль: AIM Orchestrator

**Приоритет разработки:** 3 (Средний - опциональная функциональность)  
**Зависимости:** Module_02 (EmpyrionGateway)  
**Статус:** 🟢 Спецификация готова

---

## 1. Назначение

AIM Orchestrator управляет **AI-командами (Advanced Intelligent Mechanics)** и **прямым управлением NPC** (v1.15+). Обеспечивает безопасное выполнение команд через whitelist, rate limiting и валидацию.

**Две стратегии:**
1. **AIM команды** — для сложного поведения (патрули, дроны)
2. **Прямое управление** — простое движение через IEntity API (v1.15+)

---

## 2. Интерфейсы

### 2.1 Основной интерфейс

```csharp
/// <summary>
/// Оркестратор AI-команд
/// </summary>
public interface IAIMOrchestrator
{
    // === Классические AIM команды ===
    
    Task ExecuteGuardAreaAsync(int playerId, int range);
    Task ExecuteDroneWaveAsync(int targetEntityId);
    Task ExecuteSpawnDroneBaseAsync(string playfield, Vector3 position);
    
    bool IsRateLimitReached();
    
    // === Прямое управление НПС (v1.15+) ===
    
    void MoveNPCForward(IEntity npcEntity, float speed);
    void MoveNPC(IEntity npcEntity, Vector3 direction);
    void StopNPC(IEntity npcEntity);
    void SetNPCPosition(IEntity npcEntity, Vector3 position);
}

/// <summary>
/// Валидатор команд
/// </summary>
public interface ICommandValidator
{
    bool IsCommandAllowed(string command);
    bool CanExecuteNow(string command);
}

/// <summary>
/// Простой контроллер патрулирования (v1.15+)
/// </summary>
public interface ISimplePatrolController
{
    void StartPatrol(IEntity npc, List<Vector3> waypoints, float speed = 2.0f);
    void StopPatrol(IEntity npc);
    void UpdatePatrolLogic();  // Вызывать в Core Loop Update
    
    // Предустановленные паттерны
    void PatrolCircle(IEntity npc, Vector3 center, float radius, float speed = 2.0f);
    void PatrolRectangle(IEntity npc, Vector3 corner1, Vector3 corner2, float speed = 2.0f);
}
```

---

## 3. Whitelist разрешенных команд

**Конфигурация (Configuration.json):**
```json
{
  "AIM": {
    "AllowedCommands": [
      "aim aga",    // Attack Guard Area
      "aim tdw",    // Trigger Drone Wave
      "aim adb"     // Add Drone Base
    ],
    "RateLimitPerMinute": 10,
    "MaxSimultaneousCommands": 5
  }
}
```

**Реализация валидатора:**
```csharp
public class CommandValidator : ICommandValidator
{
    private readonly HashSet<string> _allowedCommands;
    
    public bool IsCommandAllowed(string command)
    {
        var baseCommand = command.Split(' ')[0];
        return _allowedCommands.Contains(baseCommand);
    }
}
```

---

## 4. Rate Limiting

```csharp
public class AIMRateLimiter
{
    private readonly int _maxCommandsPerMinute = 10;
    private Queue<DateTime> _commandHistory = new();
    
    public bool AllowCommand()
    {
        var now = DateTime.UtcNow;
        
        // Удаляем команды старше минуты
        while (_commandHistory.Any() && 
               (now - _commandHistory.Peek()).TotalMinutes > 1)
        {
            _commandHistory.Dequeue();
        }
        
        if (_commandHistory.Count >= _maxCommandsPerMinute)
            return false;  // Rate limit exceeded
        
        _commandHistory.Enqueue(now);
        return true;
    }
}
```

---

## 5. Примеры использования

### 5.1 Классические AIM команды

```csharp
// Активация охраны области
await _aimOrchestrator.ExecuteGuardAreaAsync(
    playerId: 100,
    range: 500
);

// Волна дронов на базу игрока
await _aimOrchestrator.ExecuteDroneWaveAsync(
    targetEntityId: playerBaseId
);
```

### 5.2 Прямое управление НПС (v1.15+)

```csharp
// Переместить охранника вперед
var guardEntity = playfield.Entities[guardEntityId];
_aimOrchestrator.MoveNPCForward(guardEntity, speed: 2.5f);

// Двигать НПС в направлении цели
var direction = (targetPosition - guardEntity.Position).normalized;
_aimOrchestrator.MoveNPC(guardEntity, direction);

// Остановить НПС
_aimOrchestrator.StopNPC(guardEntity);

// Телепортировать НПС (мгновенное перемещение)
_aimOrchestrator.SetNPCPosition(guardEntity, new Vector3(1000, 150, -500));
```

### 5.3 Патрулирование (v1.15+)

```csharp
// Круговое патрулирование вокруг базы
_patrolController.PatrolCircle(
    npc: guardEntity,
    center: basePosition,
    radius: 50f,
    speed: 2.0f
);

// Патрулирование по произвольным точкам
var waypoints = new List<Vector3>
{
    new Vector3(1000, 150, -500),
    new Vector3(1050, 150, -450),
    new Vector3(1020, 150, -480)
};

_patrolController.StartPatrol(guardEntity, waypoints, speed: 2.5f);

// В Core Loop Update:
public void Update()
{
    _patrolController.UpdatePatrolLogic();  // Обновить логику всех патрулей
}
```

---

## 6. Реализация патрулирования

```csharp
public class SimplePatrolController : ISimplePatrolController
{
    private Dictionary<int, PatrolData> _activePatrols = new();
    
    public void StartPatrol(IEntity npc, List<Vector3> waypoints, float speed)
    {
        _activePatrols[npc.Id] = new PatrolData
        {
            Entity = npc,
            Waypoints = waypoints,
            Speed = speed,
            CurrentWaypointIndex = 0
        };
    }
    
    public void UpdatePatrolLogic()
    {
        foreach (var patrol in _activePatrols.Values)
        {
            var currentPos = patrol.Entity.Position;
            var targetWaypoint = patrol.Waypoints[patrol.CurrentWaypointIndex];
            
            // Проверка достижения точки
            if (Vector3.Distance(currentPos, targetWaypoint) < 2f)
            {
                patrol.CurrentWaypointIndex = (patrol.CurrentWaypointIndex + 1) % patrol.Waypoints.Count;
            }
            else
            {
                // Движение к точке
                var direction = (targetWaypoint - currentPos).normalized;
                _entityControl.MoveNPC(patrol.Entity, direction);
            }
        }
    }
    
    public void PatrolCircle(IEntity npc, Vector3 center, float radius, float speed)
    {
        // Генерация 8 точек по кругу
        var waypoints = new List<Vector3>();
        for (int i = 0; i < 8; i++)
        {
            var angle = (i / 8f) * 2f * Mathf.PI;
            waypoints.Add(center + new Vector3(
                Mathf.Cos(angle) * radius,
                0,
                Mathf.Sin(angle) * radius
            ));
        }
        
        StartPatrol(npc, waypoints, speed);
    }
}
```

---

## 7. Обработка ошибок

| Ошибка | Стратегия |
|--------|-----------|
| **Command not in whitelist** | Throw `SecurityException` |
| **Rate limit exceeded** | Return false, log warning |
| **Invalid NPC entity** | Throw `ArgumentException` |
| **ModAPI command failed** | Retry 1 раз, затем log error |

---

## 8. Чеклист разработчика

**Этап 1: Базовые AIM команды (1 день)**
- [ ] Реализовать `IAIMOrchestrator`
- [ ] Whitelist валидатор
- [ ] Rate limiter
- [ ] Unit-тесты

**Этап 2: Прямое управление НПС (1 день)**
- [ ] Интеграция с IEntity API (v1.15+)
- [ ] `MoveNPCForward()`, `StopNPC()`, etc.
- [ ] Тесты на реальном сервере

**Этап 3: Патрулирование (1 день)**
- [ ] `ISimplePatrolController`
- [ ] Круговое/прямоугольное патрулирование
- [ ] Интеграция с Core Loop

---

## 9. Известные проблемы

### 9.1 AIM команды не всегда работают

**Причина:** Зависит от конфигурации сервера и версии игры

**Решение:** Использовать прямое управление через IEntity (v1.15+) как fallback

### 9.2 NPC застревают при патрулировании

**Причина:** Нет обхода препятствий

**Решение:** Использовать AIM команды для сложного AI, прямое управление только для простых паттернов

---

## 10. Связь с другими документами

- **[Module_02_EmpyrionGateway.md](Module_02_EmpyrionGateway.md)** — отправка AIM команд через Gateway
- **[Module_10_Threat_Director.md](Module_10_Threat_Director.md)** — использует AIM для волн атак

---

**Последнее обновление:** 28.01.2026  
**Размер:** ~380 строк
