# Модуль: Empyrion Gateway

**Назначение:** Адаптер для взаимодействия с Empyrion ModAPI

---

## Ответственность

- Отправка запросов к ModAPI
- Обработка событий от ModAPI
- Управление SeqNr (sequence numbers)
- Очередь запросов и rate limiting
- Retry логика при ошибках

---

## Интерфейсы

```csharp
public interface IEmpyrionGateway
{
    // Основные методы
    Task<T> SendRequestAsync<T>(CmdId requestId, object data, int timeoutMs = 5000);
    event EventHandler<GameEventArgs> GameEventReceived;
    void Start();
    void Stop();
}

// Новые расширенные интерфейсы (v1.15+)
public interface IEntityControl
{
    // Прямое управление движением НПС/структур
    void MoveEntityForward(IEntity entity, float speed);
    void MoveEntity(IEntity entity, Vector3 direction);
    void StopEntity(IEntity entity);
    void SetEntityPosition(IEntity entity, Vector3 position);
    void SetEntityRotation(IEntity entity, Quaternion rotation);
    void DamageEntity(IEntity entity, int damageAmount, int damageType);
}

public interface IPlayfieldOperations
{
    // Получение высоты рельефа (решена проблема с эвристиками!)
    float GetTerrainHeightAt(IPlayfield playfield, float x, float z);
    
    // Блокировка устройств на структурах
    Task<bool> LockStructureDeviceAsync(int structureId, VectorInt3 posInStruct, bool doLock);
    bool IsStructureDeviceLocked(int structureId, VectorInt3 posInStruct);
}

public interface IStructureMaintenanceOperations
{
    // Обновление таймера распада структуры (защита от auto-cleanup)
    Task TouchStructureAsync(int structureId);
    
    // Получение информации о последнем посещении
    ulong GetStructureLastVisitedTicks(IStructure structure);
}

public interface IPdaOperations
{
    // Создание волн атак программно
    Task<uint> CreateWaveAttackAsync(WaveStartData waveData);
    
    // Расширенный спавн сущностей с указанием фракции
    Task<int> SpawnEntityAtPositionAsync(Vector3 position, string className, string faction, int height = 0);
    
    // Управление таймерами
    void CreateTimer(string id, float duration, Action timerAction, bool immediate = false);
    void StartTimer(string id);
    void StopTimer(string id);
}
```

---

## Зависимости

- `ModGameAPI` — Empyrion API (внешняя зависимость)
- `IRequestQueue` — очередь запросов
- `ISequenceManager` — управление SeqNr
- `IRateLimiter` — rate limiting

---

## Ключевые классы

1. **EmpyrionGateway** — главный шлюз
2. **RequestQueue** — очередь с приоритетами
3. **SequenceManager** — сопоставление запрос-ответ
4. **RateLimiter** — token bucket limiter

---

## Примеры использования

```csharp
// Спавн структуры (классический способ)
var entityId = await _gateway.SendRequestAsync<int>(
    CmdId.Request_Entity_Spawn,
    new EntitySpawnInfo { ... }
);

// Спавн НПС с указанием фракции (новый способ v1.15+)
var npcId = await _pdaOps.SpawnEntityAtPositionAsync(
    new Vector3(1000, 150, -500),
    className: "AlienAssassin",
    faction: "Zirax",
    height: 0
);

// Получение точной высоты рельефа (новое в v1.15+)
float terrainHeight = _playfieldOps.GetTerrainHeightAt(playfield, x: 1000, z: -500);

// Управление движением НПС (новое в v1.15+)
_entityControl.MoveEntityForward(guardEntity, speed: 2.5f);
_entityControl.SetEntityPosition(guardEntity, new Vector3(1000, terrainHeight + 1, -500));

// Защита структур от автоудаления (новое в v1.15+)
await _structureMaintenance.TouchStructureAsync(baseEntityId);

// Создание волны атак (новое в v1.15+)
var waveId = await _pdaOps.CreateWaveAttackAsync(new WaveStartData
{
    Name = "GLEX_DefenseWave",
    Target = playerStructureId.ToString(),
    Faction = "Zirax",
    Cost = 100
});

// Подписка на события
_gateway.GameEventReceived += (s, e) =>
{
    if (e.EventId == CmdId.Event_Player_ChangedPlayfield)
    {
        // Handle event
    }
};
```

---

## Тесты

- `Gateway_SendsRequestAndReceivesResponse()`
- `RateLimiter_BlocksExcessiveRequests()`
- `SequenceManager_MatchesRequestsToResponses()`
