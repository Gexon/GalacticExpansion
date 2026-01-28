# Модуль: AIM Orchestrator

**Назначение:** Безопасное управление AIM-командами (Advanced Intelligent Mechanics)

---

## Ответственность

- Формирование и выполнение AIM-команд
- Whitelist разрешенных команд
- Rate limiting для AIM
- Валидация параметров команд
- Логирование всех команд

---

## Интерфейсы

```csharp
public interface IAIMOrchestrator
{
    Task ExecuteGuardAreaAsync(int playerId, int range);
    Task ExecuteDroneWaveAsync(int targetEntityId);
    Task ExecuteSpawnDroneBaseAsync(string playfield, PVector3 position);
}

public interface ICommandValidator
{
    bool IsCommandAllowed(string command);
    bool CanExecuteNow(string command);
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

---

## Тесты

- `AIMOrchestrator_ExecutesAllowedCommands()`
- `AIMOrchestrator_RejectsNonWhitelistedCommands()`
- `AIMRateLimiter_PreventsSpam()`
