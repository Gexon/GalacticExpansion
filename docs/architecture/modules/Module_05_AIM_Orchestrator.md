# Модуль: AIM Orchestrator

**Назначение:** Безопасное управление AIM-командами (Advanced Intelligent Mechanics)

---

## Ответственность

- Формирование и выполнение AIM-команд для сложного поведения
- **Прямое управление движением НПС через IEntity API** ✅ (v1.15+)
- Whitelist разрешенных команд
- Rate limiting для AIM
- Валидация параметров команд
- Логирование всех команд

---

## Интерфейсы

```csharp
public interface IAIMOrchestrator
{
    // Классические AIM команды (для сложного поведения)
    Task ExecuteGuardAreaAsync(int playerId, int range);
    Task ExecuteDroneWaveAsync(int targetEntityId);
    Task ExecuteSpawnDroneBaseAsync(string playfield, PVector3 position);
    
    // Новые методы прямого управления (v1.15+)
    void MoveNPCForward(IEntity npcEntity, float speed);
    void MoveNPC(IEntity npcEntity, Vector3 direction);
    void StopNPC(IEntity npcEntity);
    void SetNPCPosition(IEntity npcEntity, Vector3 position);
    void SetNPCRotation(IEntity npcEntity, Quaternion rotation);
}

public interface ICommandValidator
{
    bool IsCommandAllowed(string command);
    bool CanExecuteNow(string command);
}

// Новый контроллер для простого патрулирования (v1.15+)
public interface ISimplePatrolController
{
    void StartPatrol(IEntity npcEntity, List<Vector3> waypoints, float speed = 2.0f);
    void StopPatrol(IEntity npcEntity);
    void UpdatePatrolLogic();  // Вызывается в Core Loop Update
    
    // Предустановленные паттерны
    void PatrolCircle(IEntity npcEntity, Vector3 center, float radius, float speed = 2.0f);
    void PatrolRectangle(IEntity npcEntity, Vector3 corner1, Vector3 corner2, float speed = 2.0f);
}
```

---

## Зависимости

- `IEmpyrionGateway` — для отправки команд через ConsoleCommand
- `IRateLimiter` — для AIM rate limiting
- `ICommandValidator` — для валидации

---

## Ключевые классы

1. **AIMOrchestrator** — основной оркестратор
2. **CommandValidator** — whitelist и валидация
3. **AIMRateLimiter** — специализированный limiter

---

## Примеры использования

```csharp
// === Классические AIM команды (для сложного поведения) ===

// Активация охраны области
await _aimOrchestrator.ExecuteGuardAreaAsync(
    playerId: 100,
    range: 500
);

// Волна дронов на базу игрока
await _aimOrchestrator.ExecuteDroneWaveAsync(
    targetEntityId: playerBaseId
);

// === Новое прямое управление НПС (v1.15+) ===

// Переместить охранника вперед
var guardEntity = playfield.Entities[guardEntityId];
_aimOrchestrator.MoveNPCForward(guardEntity, speed: 2.5f);

// Двигать НПС в направлении цели
var directionToTarget = (targetPosition - guardEntity.Position).normalized;
_aimOrchestrator.MoveNPC(guardEntity, directionToTarget);

// Остановить НПС
_aimOrchestrator.StopNPC(guardEntity);

// Телепортировать НПС (мгновенное перемещение)
_aimOrchestrator.SetNPCPosition(guardEntity, new Vector3(1000, 150, -500));

// === Патрулирование (v1.15+) ===

// Круговое патрулирование вокруг базы
_patrolController.PatrolCircle(
    npcEntity: guardEntity,
    center: basePosition,
    radius: 50f,
    speed: 2.0f
);

// Патрулирование по прямоугольной области
_patrolController.PatrolRectangle(
    npcEntity: guardEntity,
    corner1: new Vector3(1000, 150, -500),
    corner2: new Vector3(1100, 150, -400),
    speed: 2.0f
);

// Патрулирование по произвольным точкам
var waypoints = new List<Vector3>
{
    new Vector3(1000, 150, -500),
    new Vector3(1050, 150, -450),
    new Vector3(1020, 150, -480),
    new Vector3(1030, 150, -510)
};

_patrolController.StartPatrol(guardEntity, waypoints, speed: 2.5f);

// В Core Loop Update:
public void Update()
{
    // Обновить логику патрулирования для всех активных НПС
    _patrolController.UpdatePatrolLogic();
}
```

---

## Тесты

- `AIMOrchestrator_ExecutesAllowedCommands()`
- `AIMOrchestrator_RejectsNonWhitelistedCommands()`
- `AIMRateLimiter_PreventsSpam()`
