# План модульной разработки (GLEX)

**Назначение**: декомпозиция “модульного монолита” GLEX на модули с явными границами и зависимостями.  
**Аудитория**: разработчики.  
**Статус**: draft.  
**Связанные документы**: [`02_Архитектурный_план.md`](02_Архитектурный_план.md), [`modules/README.md`](modules/README.md).  

## Зачем модульность

Мы строим **модульный монолит**:

- уменьшаем когнитивную нагрузку (каждый модуль — законченная зона ответственности),
- уменьшаем неявные зависимости,
- оставляем путь к будущему выделению отдельных частей (если потребуется).

## Модули (верхний уровень)

Подробные документы — в `docs/architecture/modules/`.

- `SimulationEngine` — цикл симуляции, планирование действий по времени/событиям.
- `EmpyrionGateway` — адаптер ModAPI: очередь requests, `seqNr`, retry/dedup, rate limit.
- `StateStore` — загрузка/сохранение `state.json`, миграции схемы.
- `PlacementResolver` — выбор координат спавна и эвристики “поверхности/безопасности”.
- `SpawningAndEvolution` — спавн/удаление сущностей, staged-evolution, замена prefab’ов.
- `AimOrchestrator` — безопасная отправка `aim ...` через `Request_ConsoleCommand`.
- `PlayerTracker` — события/списки игроков, привязка контекста “игроки на playfield”.
- `StructureTracker` — индексация структур, сверка списков, детект разрушений.
- `ThreatDirector` — правила угроз (патрули/волны) по ситуации.
- `EconomySim` — виртуальные ресурсы/производство/логистика.
- `ExpansionPlanner` — правила экспансии на новые планеты.

## Порядок реализации (MVP)

Минимально необходимая цепочка:

1) `EmpyrionGateway` + базовый `SimulationEngine` (тик/таймер).
2) `StateStore` (state.json v1) + атомарная запись.
3) `PlayerTracker` (для условий “есть игроки на playfield”).
4) `PlacementResolver` (простые эвристики + фоллбек).
5) `SpawningAndEvolution` (spawn/destroy, стадии базы).
6) `AimOrchestrator` (строго whitelist + rate limit).
7) `StructureTracker` (чтобы детектить разрушение и откатывать стадии).
8) `ThreatDirector` (минимальные патрули/волны в MVP).

## Инварианты (правила, которые нельзя нарушать)

- Любой вызов `Request_ConsoleCommand` идёт через `AimOrchestrator` и проверяется whitelist’ом.
- Любой массовый опрос мира (`Request_GlobalStructure_List` и т.п.) — ограничен по частоте.
- `StateStore` пишет состояние атомарно: `write tmp → rename`.
