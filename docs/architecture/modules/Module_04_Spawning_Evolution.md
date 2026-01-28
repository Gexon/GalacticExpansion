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
    // Базовые методы спавна
    Task<int> SpawnStructureAsync(string prefabName, PVector3 position, PVector3 rotation, int factionId);
    Task<List<int>> SpawnNPCGroupAsync(string npcType, PVector3 position, int count, int factionId);
    Task DestroyEntityAsync(int entityId);
    
    // Новые методы (v1.15+)
    Task<int> SpawnNPCAtTerrainAsync(string npcClassName, float x, float z, string faction);
    Task<int> SpawnStructureAtTerrainAsync(string prefabName, float x, float z, int factionId, float heightOffset = 0);
}

public interface IStageManager
{
    Task<bool> CanTransitionToNextStage(Colony colony);
    Task TransitionToStageAsync(Colony colony, ColonyStage newStage);
    
    // Защита структур от распада
    Task MaintainColonyStructuresAsync(Colony colony);
}

public interface INPCController
{
    // Управление НПС (v1.15+)
    void MoveNPCToPosition(IEntity npc, Vector3 targetPosition, float speed = 2.0f);
    void PatrolArea(IEntity npc, Vector3 center, float radius);
    void GuardPosition(IEntity npc, Vector3 position, float alertRadius);
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
// Спавн базы с автоматическим определением высоты (v1.15+)
var entityId = await _spawner.SpawnStructureAtTerrainAsync(
    prefabName: "GLEX_Base_L1",
    x: 1000,
    z: -500,
    factionId: 2,
    heightOffset: 0.5f // небольшой отступ над землей
);

// Спавн НПС-охранников с точным размещением (v1.15+)
var guardId = await _spawner.SpawnNPCAtTerrainAsync(
    npcClassName: "AlienAssassin",
    x: 1050,
    z: -450,
    faction: "Zirax"
);

// Управление патрулированием НПС (v1.15+)
var guardEntity = playfield.Entities[guardId];
_npcController.PatrolArea(guardEntity, center: basePosition, radius: 50f);

// Переход на следующую стадию с защитой от распада
if (await _stageManager.CanTransitionToNextStage(colony))
{
    await _stageManager.TransitionToStageAsync(colony, ColonyStage.BaseL2);
    
    // Обновить таймер распада для новой структуры
    await _stageManager.MaintainColonyStructuresAsync(colony);
}

// Периодическое обслуживание колоний (предотвращение auto-cleanup)
foreach (var colony in activeColonies)
{
    await _stageManager.MaintainColonyStructuresAsync(colony);
}
```

---

## Тесты

- `EntitySpawner_SpawnsStructureCorrectly()`
- `StageManager_TransitionsWhenConditionsMet()`
- `StageManager_DoesNotTransitionPrematurely()`
