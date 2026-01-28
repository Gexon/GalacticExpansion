# System Patterns — GalacticExpansion (GLEX)

## Архитектурные паттерны

### Модульный монолит

Система построена как единое приложение (DLL) с четко выделенными модулями.

**Преимущества:**
- Простота развертывания (одна DLL)
- Четкие границы между модулями
- Возможность будущего выделения в микросервисы

**Ключевые модули:**
- Core Loop (SimulationEngine)
- Empyrion Gateway (API Adapter)
- State Store (Persistence)
- Spawning & Evolution
- AIM Orchestrator
- Placement Resolver

### Event-Driven Architecture

Модули общаются через внутреннюю шину событий (EventBus).

**Примеры событий:**
- `PlayerEnteredPlayfieldEvent`
- `ColonyUpgradedEvent`
- `StructureDestroyedEvent`

**Преимущества:**
- Слабая связь между модулями
- Легко добавлять новых подписчиков
- Асинхронная обработка

### Repository Pattern

StateStore использует паттерн Repository для персистентности.

```csharp
interface IStateStore {
    Task<SimulationState> LoadAsync();
    Task SaveAsync(SimulationState state);
}
```

### Command Pattern

AIM-команды обрабатываются через Command Pattern.

```csharp
interface IAIMCommand {
    string CommandName { get; }
    Task ExecuteAsync();
}
```

---

## Ключевые технические решения

### Атомарная запись state.json

**Проблема:** Крашт во время записи = коррупция данных

**Решение:** Write temp → Rename
```csharp
File.WriteAllText("state.json.tmp", json);
File.Replace("state.json.tmp", "state.json", null);
```

### Асинхронный Request-Response

**Проблема:** ModAPI асинхронный (callback-based)

**Решение:** TaskCompletionSource + SeqNr

```csharp
var promise = new TaskCompletionSource<int>();
_pendingResponses[seqNr] = promise;
GameAPI.Game_Request(cmd, seqNr, data);
return await promise.Task;
```

### Rate Limiting (Token Bucket)

**Проблема:** Спам API запросами → перегрузка сервера

**Решение:** Token Bucket Algorithm
```csharp
if (_tokens > 0) {
    _tokens--;
    return true; // Allow
}
return false; // Deny
```

### Circuit Breaker

**Проблема:** ModAPI может быть недоступен

**Решение:** Circuit Breaker Pattern
- После 5 failures подряд → Open (блокировка запросов)
- Через 60 секунд → HalfOpen (пробная попытка)
- Если успех → Closed (нормальная работа)

### Finite State Machine для стадий

**Проблема:** Управление переходами между стадиями колоний

**Решение:** FSM
```
LandingPending → ConstructionYard → BaseL1 → BaseL2 → BaseL3 → BaseMax
```

С правилами переходов:
- Требуется накопление ресурсов
- Минимальное время в стадии
- Возможность отката при разрушении

---

## Паттерны безопасности

### Whitelist консольных команд

Только разрешенные AIM-команды могут быть выполнены:
```csharp
AllowedCommands = { "aim aga", "aim tdw", "aim adb" };
```

### Санитизация входных данных

Все данные из конфигурации валидируются:
```csharp
if (value < min) value = min;
if (value > max) value = max;
```

### Логирование security events

Все AIM-команды и критичные операции логируются:
```csharp
_logger.LogWarning($"AIM command executed: {cmd} by {context}");
```

---

## Паттерны производительности

### Кэширование

Часто запрашиваемые данные кэшируются:
- Список структур (10 секунд)
- Список игроков (до события ChangedPlayfield)

### Асинхронность

Все долгие операции асинхронные:
- API запросы (async/await)
- Сохранение state.json (Task.Run)
- Background обработка (ThreadPool)

### Lazy Loading

Модули инициализируются только при необходимости.

---

## Паттерны тестирования

### Dependency Injection для тестируемости

Все зависимости через интерфейсы:
```csharp
public class SimulationEngine(IStateStore store, IEventBus bus) { }
```

В тестах — моки:
```csharp
var mockStore = new Mock<IStateStore>();
```

### Time Provider для детерминированных тестов

Время мокается:
```csharp
interface ITimeProvider {
    DateTime UtcNow { get; }
}
```

### Seeded Random для воспроизводимости

```csharp
var placement = new PlacementResolver(seed: 42);
```

---

## Важные инварианты

1. **State.json всегда валиден** (атомарная запись + бэкапы)
2. **SeqNr уникальны** (инкремент с overflow check)
3. **Rate limits соблюдаются** (token bucket enforced)
4. **Whitelist строг** (никаких исключений)
5. **Modules независимы** (нет direct dependencies, только через interfaces)

---

## Anti-patterns (чего избегать)

❌ **Прямые вызовы между модулями** → Используй EventBus  
❌ **Синхронные API вызовы** → Всегда async/await  
❌ **Хардкод значений** → Все через конфигурацию  
❌ **Игнорирование ошибок** → Всегда try-catch + логирование  
❌ **Блокирующие операции в simulation tick** → Offload в background
