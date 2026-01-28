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
    Task<T> SendRequestAsync<T>(CmdId requestId, object data, int timeoutMs = 5000);
    event EventHandler<GameEventArgs> GameEventReceived;
    void Start();
    void Stop();
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
// Спавн структуры
var entityId = await _gateway.SendRequestAsync<int>(
    CmdId.Request_Entity_Spawn,
    new EntitySpawnInfo { ... }
);

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
