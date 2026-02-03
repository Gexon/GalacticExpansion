# Progress — GalacticExpansion (GLEX)

## Текущий статус

**Дата обновления:** 03.02.2026  
**Phase 3 (Domain):** завершена — логика, тесты, runtime (ColonyTickModule, первая колония по Event_Playfield_Loaded, обновление по тику)  
**Phase 4 (Combat):** не начата

## Недавний прогресс

- **Runtime Phase 3:** ColonyTickModule при пустом state и включённой экспансии ждёт `Event_Playfield_Loaded` (тип данных `PlayfieldLoad`) для `HomePlayfield` и создаёт первую колонию только после загрузки нужного playfield; далее обновляет колонии по тику через ColonyManager.
- **EmpyrionGateway:** обработка `CmdId.Event_Error` — завершение ожидающего запроса через `CompleteWithError` с текстом из `ErrorInfo` (в т.ч. `errorType`), чтобы в логах и исключениях был понятный код ошибки.
- **PlacementResolver:** поздняя установка `IModApi` через `SetModApi` в ModMain.Init.
- **Конфиг:** дефолтные Zirax.Stages/DropShips с ванильными префабами в ConfigurationLoader; StageManager использует префаб из конфига или BA_ConstructionSite.
- **Документация:** инструкция для тестера в `docs/manuals/Tester_Manual_Colony_Access.md`.
- Исправлены unit/integration тесты (SimulationEngine `LoadAsync` как минимум 2 раза, последующие вызовы при тиках; PlacementResolver GlobalStructureList; StageManager/EntitySpawner контракты).

## Тестирование

- Unit: проходят ✅  
- Integration: проходят ✅  
- Команда: `dotnet test src/GalacticExpansion.sln --configuration Release`

## Что работает

- Core Loop, Gateway, State Store, Trackers — стабильно.
- Phase 3 Domain: Spawning, Placement, Economy, Unit Economy, StageManager, ColonyManager — тесты проходят.
- Создание первой колонии по `Event_Playfield_Loaded` для `HomePlayfield` (при включённой экспансии в конфиге) и обновление колоний по тику симуляции.

## Что дальше

1. Phase 3.5: server testing на dedicated server (deploy → проверка логов, появление колоний, спавн структур; при Event_Error — смотреть код ErrorType в логах).
2. Phase 4: Threat Director + AIM Orchestrator (по архитектурной документации).
