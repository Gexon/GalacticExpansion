# Модуль: Core Loop & Simulation Engine

**Назначение:** Главный цикл симуляции, координирующий работу всех модулей

---

## Ответственность

- Управление жизненным циклом симуляции (Start/Stop)
- Периодическое обновление состояния (Simulation Tick)
- Координация работы подмодулей
- Управление временем симуляции
- Регистрация и оркестрация модулей

---

## Интерфейсы

```csharp
public interface ISimulationEngine
{
    Task StartAsync();
    Task StopAsync();
    void RegisterModule(ISimulationModule module);
}

public interface ISimulationModule
{
    string ModuleName { get; }
    Task InitializeAsync(SimulationState state);
    void OnSimulationUpdate(SimulationContext context);
    Task ShutdownAsync();
}
```

---

## Зависимости

- `IStateStore` — для загрузки/сохранения состояния
- `IModuleRegistry` — для управления модулями
- `IEventBus` — для внутренних событий

---

## Ключевые классы

1. **SimulationEngine** — главный движок
2. **ModuleRegistry** — реестр модулей
3. **EventBus** — шина событий
4. **SimulationContext** — контекст обновления

---

## Примеры использования

```csharp
// Регистрация модуля
_engine.RegisterModule(new ColonyManager());
_engine.RegisterModule(new ThreatDirector());

// Запуск симуляции
await _engine.StartAsync();
```

---

## Тесты

- `SimulationEngine_StartsAndStopsCorrectly()`
- `SimulationEngine_UpdatesModulesEveryTick()`
- `EventBus_PublishesEventsToSubscribers()`
