# Tech Context — GalacticExpansion (GLEX)

## Стек

- C# 8.0+, .NET Framework 4.8
- Newtonsoft.Json 13.x
- NLog 5.x
- xUnit 2.6+, Moq 4.20+

## Внешние зависимости (Empyrion)

- `ModApi.dll`, `Mif.dll`, `protobuf-net.dll`

## Ключевые ModAPI возможности

- Спавн/удаление сущностей: `Request_Entity_Spawn`, `Request_Entity_Destroy`
- Список структур: `Request_GlobalStructure_List`
- Защита от decay: `Request_Structure_Touch`
- Высота рельефа: `IPlayfield.GetTerrainHeightAt(x, z)` (через `IPlayfieldWrapper`)

## Структура проекта

- `src/GalacticExpansion.Core` — модули
- `src/GalacticExpansion.Models` — модели данных
- `src/GalacticExpansion` — entry point
- `src/GalacticExpansion.Tests.Unit`, `src/GalacticExpansion.Tests.Integration`

## Локальная разработка

- Сборка: `dotnet build src/GalacticExpansion.sln --configuration Release`
- Тесты: `dotnet test src/GalacticExpansion.sln --configuration Release`

## Логирование

- NLog, путь к конфигу задается явно в `ModMain`.

## ModAPI: ответы с ошибкой

- При ошибке запроса игра возвращает `CmdId.Event_Error`, данные — `ErrorInfo` (поле `errorType` типа `ErrorType`). В логах и исключениях используется `errorType.ToString()` (например, `EntityNotLocalToPlayfield`, `PlayfieldConnectionNotFound`). См. `EmpyrionGateway.HandleEvent` и `GetErrorMessageFromErrorInfo`.

## ModAPI: загрузка playfield

- Событие `CmdId.Event_Playfield_Loaded` использует тип данных `PlayfieldLoad` (из `Mif.dll` / `Eleon.Modding`), содержащий:
  - `sec` — время загрузки playfield в секундах,
  - `playfield` — имя загруженного playfield,
  - `processId` — ID процесса/инстанса.
- В `ColonyTickModule` данные события декодируются как `PlayfieldLoad`, имя playfield берётся из поля `playfield` и используется для:
  - отложенного создания первой колонии на `HomePlayfield` (Phase 3.5, первая физическая база появляется только после загрузки нужного playfield),
  - вызова `IColonyManager.EnsurePlayfieldColoniesSpawnedAsync(playfield)` для обновления/защиты структур колоний на этом playfield.

## Известные нюансы

- В тестах использовать `IPlayfieldWrapper` вместо `IPlayfield`.
- Контракты интерфейсов должны совпадать с реализациями (порядок параметров важен).
- `IPlacementResolver.SetModApi(IModApi?)` вызывается в ModMain.Init для поздней инъекции ModApi (точная высота рельефа).
- Дефолтные префабы колоний — ванильные (BA_ConstructionSite, BA_Zirax_*, BA_MiningOutpost_Zirax_1); задаются в ConfigurationLoader при отсутствии в конфиге.
