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
- Economy Simulator
- Unit Economy Manager
- Threat Director
- Hostility Tracker

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
LandingPending → ConstructionYard → BaseL1 → BaseL2 → BaseL3 → BaseMax → Expansion
```

С правилами переходов:
- Требуется накопление ресурсов
- Минимальное время в стадии
- Возможность отката при разрушении
- Экспансия в направлении родной планеты Most Wanted врага

### Most Wanted Priority System

**Проблема:** Как определить цель охоты среди множества враждебных игроков?

**Решение:** Двухуровневая приоритизация
```csharp
// Приоритет 1: Самый враждебный ОНЛАЙН игрок
var onlineHostile = GetHostilePlayers()
    .Where(p => p.IsOnline)
    .OrderByDescending(p => p.HostilityScore)
    .FirstOrDefault();

// Приоритет 2: Топовый оффлайн (только для экспансии)
if (onlineHostile == null)
    return GetMostHostileOfflinePlayer();
```

**Преимущества:**
- Охота активна только против онлайн игроков
- Топовый оффлайн игрок не подвергается постоянным атакам
- Экспансия может быть направлена на оффлайн врага
- Динамическая адаптация под активность игроков

### Home Planet Detection Pattern

**Проблема:** Как определить родную планету игрока для целевой экспансии?

**Решение:** Анализ структур игрока
```csharp
var homePlanet = playerStructures
    .GroupBy(s => s.Playfield)
    .OrderByDescending(g => g.Count())
    .ThenByDescending(g => g.Sum(s => s.BlockCount))
    .First()
    .Key;
```

**Критерии:**
1. Количество структур на планете (основной фактор)
2. Суммарное количество блоков (вторичный фактор)
3. Кэширование результата в PlayerHostilityInfo

### Gradual Expansion Path Pattern

**Проблема:** Как создать эффект надвигающейся угрозы вместо мгновенного прыжка на родную планету игрока?

**Решение:** Постепенная экспансия через граф планет
```csharp
// Рассчитываем путь от текущей колонии к цели
var path = CalculateShortestPath(currentPlanet, targetPlanet, playfieldGraph);

// Берем следующую планету на пути (не прыгаем сразу!)
var nextTarget = path[1]; // [0] = current, [1] = next step

// Эффект: планета за планетой, система за системой
```

**Преимущества:**
1. Создает ощущение надвигающейся неизбежности (6+ часов)
2. Игрок видит прогресс экспансии на карте
3. Игрок может попытаться остановить (разрушая промежуточные колонии)
4. Разрушение колоний → больше враждебность → еще больше мотивации

### Hostility Decay Only on Combat Death

**Проблема:** Как сделать затухание враждебности честным и логичным?

**Решение:** Снижение только при убийстве юнитами колонии
```csharp
// НЕ просто смерть, а убийство юнитами колонии!
public void OnPlayerKilledByColony(int playerId, int killerNpcId, string colonyId)
{
    data.HostilityScore *= 0.95f; // -5%
}
```

**Логика:**
- Смерть от падения/голода/PvP → НЕ снижает
- Убийство юнитами конкретной колонии → снижает враждебность к ней
- Стимулирует сражение с колонией, а не избегание

### Unit Pool Management Pattern

**Проблема:** Как сделать юниты конечными и ощутимыми, чтобы игрок чувствовал последствия потерь?

**Решение:** Система резервов и производства с течением времени
```csharp
public class UnitPool
{
    public int AvailableGuards { get; set; }  // Резерв для немедленного спавна
    public int MaxGuards { get; set; }         // Максимальная вместимость
    public float ProductionRate { get; set; } // Юнитов в час
    public float ProductionProgress { get; set; } // Накопленный прогресс
    public List<ActiveUnit> ActiveUnits { get; set; } // Активные в мире
}

// Производство с течением времени
public void ProduceUnits(Colony colony, float deltaTime)
{
    var rate = CalculateProductionRate(colony);
    colony.UnitPool.ProductionProgress += (rate / 3600f) * deltaTime;
    
    if (colony.UnitPool.ProductionProgress >= 1.0f)
    {
        var units = (int)colony.UnitPool.ProductionProgress;
        colony.UnitPool.ProductionProgress -= units;
        DistributeProducedUnits(colony, units);
    }
}
```

**Ключевые принципы:**
1. **Reserve before spawn:** Резервируем юниты до спавна, не после
2. **Track active units:** Регистрируем активные юниты для учета потерь
3. **No instant respawn:** Потерянный юнит НЕ возвращается, нужно ждать производства
4. **Production modifiers:** Разрушение инфраструктуры снижает производство

**Формула производства:**
```
ProductionRate = BaseRate × (1 + OutpostCount × 0.25) × 
                 ShipyardBonus × DroneBaseBonus × AttackPenalty

Модификаторы:
- Каждый аванпост: +25%
- Верфь: +50%
- Дронбаза: +100%
- Под атакой: -30%
```

**Преимущества:**
- Конечность юнитов создает стратегическую глубину
- Разрушение инфраструктуры имеет долгосрочные последствия
- Игрок видит результат своих действий (база обескровлена)
- Балансируется через конфигурацию

### Reservation Pattern for Units

**Проблема:** Как гарантировать, что юниты не будут заспавнены дважды при параллельных запросах?

**Решение:** Three-phase spawn protocol
```csharp
// Phase 1: Check availability
if (!_unitEconomy.CanSpawnUnit(colony, UnitType.Guard, 10))
{
    // Reduce request or abort
}

// Phase 2: Reserve units (atomic operation)
if (!_unitEconomy.ReserveUnits(colony, UnitType.Guard, 10))
{
    // Already reserved by another request
    return;
}

// Phase 3: Spawn and register
var spawnedIds = await _entitySpawner.SpawnNPCGroupAsync(...);
foreach (var id in spawnedIds)
{
    _unitEconomy.RegisterActiveUnit(colony, id, UnitType.Guard, "Defender");
}
```

**Гарантии:**
- Резервирование атомарно (уменьшает счетчик доступных)
- Неудачный спавн НЕ возвращает юниты (потеря считается)
- Успешный спавн регистрирует юнит для отслеживания

### Production Distribution Priority Pattern

**Проблема:** В каком порядке пополнять разные типы юнитов при производстве?

**Решение:** Priority-based distribution
```csharp
private void DistributeProducedUnits(Colony colony, int totalUnits)
{
    var remaining = totalUnits;
    
    // Priority 1: Guards (основная защита)
    remaining = FillUnitType(colony.UnitPool.AvailableGuards, 
                            colony.UnitPool.MaxGuards, remaining);
    
    // Priority 2: Drones (волны атак)
    remaining = FillUnitType(colony.UnitPool.AvailableDrones,
                            colony.UnitPool.MaxDrones, remaining);
    
    // Priority 3: PatrolVessels (if stage allows)
    if (colony.Stage >= ColonyStage.BaseL1)
        remaining = FillUnitType(...);
    
    // Priority 4: Warships (if stage allows)
    if (colony.Stage >= ColonyStage.BaseL2)
        remaining = FillUnitType(...);
}
```

**Приоритеты:**
1. Guards — базовая защита (всегда нужны)
2. Drones — волны атак (дешевые, массовые)
3. PatrolVessels — патрулирование (средняя стоимость)
4. Warships — тяжелая артиллерия (дорогие, требуют верфь)

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
6. **Враждебность снижается только при убийстве юнитами колонии** (5%, смерть от других причин не учитывается)
7. **Охота только на онлайн игроков** (оффлайн топ для экспансии)
8. **Родная планета определяется по структурам** (max count → max blocks)
9. **Экспансия постепенная** (планета за планетой, не прямой прыжок на цель)
10. **Юниты резервируются перед спавном** (CanSpawn → Reserve → Spawn → Register)
11. **Потерянные юниты НЕ возвращаются** (нужно ждать производства новых)
12. **AvailableUnits ≤ MaxUnits** (всегда соблюдается при пересчете вместимости)
13. **ProductionRate ≥ 0** (никогда не отрицательная, даже при всех штрафах)

---

## Anti-patterns (чего избегать)

❌ **Прямые вызовы между модулями** → Используй EventBus  
❌ **Синхронные API вызовы** → Всегда async/await  
❌ **Хардкод значений** → Все через конфигурацию  
❌ **Игнорирование ошибок** → Всегда try-catch + логирование  
❌ **Блокирующие операции в simulation tick** → Offload в background  
❌ **Спавн без резервирования** → Всегда Reserve перед Spawn  
❌ **Мгновенное восполнение юнитов** → Используй производство с течением времени  
❌ **Игнорирование разрушений инфраструктуры** → Пересчитывай ProductionRate  
❌ **Возврат юнитов при неудачном спавне** → Потери считаются окончательными
