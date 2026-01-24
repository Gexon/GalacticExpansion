# Tech Context — GLEX

## Платформа

- ОС разработки: Windows.
- Тип проекта: server-side DLL мод для Empyrion: Galactic Survival (dedicated server).

## Интеграция с игрой

- Используется ModAPI (`ModInterface`, `ModGameAPI`).
- Основные взаимодействия:
  - события: `Game_Event(CmdId, seqNr, object data)`
  - запросы: `Game_Request(CmdId, seqNr, object data)`

## Зависимости (ориентир по примерам)

В примерах модов встречаются ссылки на:

- `Mif.dll`
- `ModApi.dll`
- `protobuf-net.dll`

Также встречаются вспомогательные фреймворки/подходы (референсы):

- EmpyrionAPITools (упрощает работу с events/requests и `seqNr`)

## Документация по сборке/деплою

См. `docs/getting-started/*` и `docs/ops/*`.