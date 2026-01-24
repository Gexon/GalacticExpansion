# Модуль `SpawningAndEvolution`

**Ответственность**: спавн/удаление сущностей и стадийное развитие (evolution) через замену prefab’ов.  
**Аудитория**: разработчики.  
**Статус**: draft.  
**Связанные документы**: [`../../design/02_stages_and_prefabs.md`](../../design/02_stages_and_prefabs.md), [`../03_Технический_проект.md`](../03_Технический_проект.md).  

## Входы

- Команды от `SimulationEngine`: “создать колонию”, “построить стройплощадку”, “апгрейднуть стадию”.
- Контекст размещения от `PlacementResolver`.

## Выходы

- Requests на `Request_NewEntityId`, `Request_Entity_Spawn`, `Request_Entity_Destroy*`.
- Обновления ссылок entityId в `state.json`.

## Инварианты

- Смена стадии делается через **destroy+spawn** (надёжный вариант из требований).
- Любой spawn учитывает лимиты (квоты) и логируется.