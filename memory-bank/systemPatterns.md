# System Patterns — GLEX

## Архитектурный стиль

- **Модульный монолит**: модули с явными границами, но единый процесс (DLL).
- **Event/Request интеграция**: вход — `Game_Event`, выход — `Game_Request`.

## Ключевые паттерны

### Шлюз ModAPI (Gateway)

- Вся работа с `CmdId + object data` централизована в `EmpyrionGateway`.
- Корреляция `request → response` по `seqNr`.
- Rate limit/дедупликация — на уровне шлюза.

### Staged evolution через prefab replace

- Смена стадии: **destroy + spawn** (надёжный вариант, зафиксирован ADR-0001).
- Связка с `state.json`: entityId’ы должны обновляться строго и атомарно.

### Безопасный AIM

- Все `Request_ConsoleCommand` проходят через `AimOrchestrator`.
- Whitelist + rate limit (ADR-0003).

### Атомарное состояние

- `state.json` пишется атомарно (ADR-0002).

## Документация-источник

Подробности в `docs/architecture/*` и `docs/adr/*`.