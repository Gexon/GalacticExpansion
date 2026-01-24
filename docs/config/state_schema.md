# Схема `state.json` (GLEX)

**Назначение**: описать структуру состояния симуляции, которое переживает перезапуски сервера.  
**Аудитория**: разработчики.  
**Статус**: draft.  
**Связанные документы**: [`../architecture/04_data_model.md`](../architecture/04_data_model.md).  

## Общие правила

- `state.json` содержит поле `schemaVersion` (int).
- Любая несовместимость схемы решается миграциями (см. `docs/architecture/04_data_model.md`).
- Запись состояния атомарная: `tmp → rename`.

## Предлагаемая структура (v1, черновик)

```json
{
  "schemaVersion": 1,
  "homePlayfield": "Akua",
  "colonies": [
    {
      "colonyId": "home-1",
      "playfield": "Akua",
      "stage": "LandingPending",
      "virtualResources": 0,
      "entities": {
        "dropshipEntityId": 0,
        "yardEntityId": 0,
        "baseEntityId": 0,
        "outpostEntityIds": []
      },
      "timers": {
        "stageStartedAtUtc": "2026-01-01T00:00:00Z",
        "lastTickAtUtc": "2026-01-01T00:00:00Z"
      },
      "flags": {
        "rebuildPending": false,
        "expansionEnabled": false
      }
    }
  ]
}
```

## Что можно/нельзя править вручную

Можно (при остановленном сервере и с бэкапом):

- `homePlayfield`
- флаги включения механик (если они будут в state)

Нельзя (опасно):

- `entities.*EntityId` (можно легко “рассинхронизировать” симуляцию и мир)
- `schemaVersion` (без миграции)
