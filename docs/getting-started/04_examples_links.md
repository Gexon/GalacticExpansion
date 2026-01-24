# Референсы из `docs/examples/` (что смотреть и зачем)

**Назначение**: перечислить конкретные примеры и какие паттерны из них мы берём.  
**Аудитория**: разработчики.  
**Статус**: draft.  

## Базовый цикл ModAPI (events/requests)

- `docs/examples/sample-empyrion-mod-master/DeathMessenger/DeathMessages.cs`
  - минимальный пример `ModInterface` с `Game_Start`, `Game_Event`, `Game_Exit`
  - отправка сообщений через `Request_ConsoleCommand` и `Request_InGameMessage_AllPlayers`
  - трекинг игроков через `Event_Player_Connected` + `Request_Player_Info`

## Паттерн конфигов (указатель `ModAppConfig.json` → основной конфиг)

- `docs/examples/EmpyrionTeleporter-master/EmpyrionTeleporter/ModAppConfig.json`
- `docs/examples/EmpyrionStructureCleanUp-master/EmpyrionStructureCleanUp/ModAppConfig.json`

Почему важно для GLEX: нам нужно хранить “глобальную” конфигурацию мода и состояние симуляции так, чтобы это было удобно для сервера и переносимо между обновлениями.

## Практика хранения состояния/миграций

- `docs/examples/EmpyrionTeleporter-master/EmpyrionTeleporter/TeleporterDB.cs`
  - пример конфигурационного менеджера, переноса старого формата (XML) в новый (JSON), автосохранения
  - полезно как референс для будущих миграций `state.json` в GLEX

## Отдельный процесс-хост (для отладки/запуска модов)

- `docs/examples/EmpyrionModHost-master/README.md`
- `docs/examples/EmpyrionModHost-master/EmpyrionModHost/Program.cs`

Почему это важно: такие решения помогают с “горячим” старт/стоп и отладкой, но это **вне MVP GLEX** — используем как справку.

## Упрощение работы с ModAPI (framework-референс)

- `docs/examples/EmpyrionAPITools-master/README.md`

Зачем: там хорошо объяснены проблемы “сырого” `CmdId + object data` и паттерн `seqNr`, которые в GLEX нужно учесть при проектировании шлюза запросов/событий.
