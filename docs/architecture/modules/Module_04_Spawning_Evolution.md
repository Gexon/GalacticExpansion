# Модуль: Spawning & Evolution

**Назначение:** Управление созданием, удалением и эволюцией сущностей

---

## Ответственность

- Спавн структур (BA, CV, SV)
- Спавн NPC (охранники, строители)
- Удаление сущностей
- Управление стадиями колоний (Stage Manager)
- Переходы между стадиями

---

## Интерфейсы

```csharp
public interface IEntitySpawner
{
    Task<int> SpawnStructureAsync(string prefabName, PVector3 position, PVector3 rotation, int factionId);
    Task<List<int>> SpawnNPCGroupAsync(string npcType, PVector3 position, int count, int factionId);
    Task DestroyEntityAsync(int entityId);
}

public interface IStageManager
{
    Task<bool> CanTransitionToNextStage(Colony colony);
    Task TransitionToStageAsync(Colony colony, ColonyStage newStage);
}
```

---

## Зависимости

- `IEmpyrionGateway` — для спавна через API
- `IPlacementResolver` — для поиска мест
- `IStateStore` — для обновления состояния

---

## Ключевые классы

1. **EntitySpawner** — фабрика сущностей
2. **StageManager** — FSM для стадий
3. **PrefabRegistry** — реестр префабов

---

## Примеры использования

```csharp
// Спавн базы
var entityId = await _spawner.SpawnStructureAsync(
    "GLEX_Base_L1",
    new PVector3(1000, 150, -500),
    new PVector3(0, 45, 0),
    factionId: 2
);

// Переход на следующую стадию
if (await _stageManager.CanTransitionToNextStage(colony))
{
    await _stageManager.TransitionToStageAsync(colony, ColonyStage.BaseL2);
}
```

---

## Тесты

- `EntitySpawner_SpawnsStructureCorrectly()`
- `StageManager_TransitionsWhenConditionsMet()`
- `StageManager_DoesNotTransitionPrematurely()`
