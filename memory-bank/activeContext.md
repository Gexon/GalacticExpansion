# Active Context — GalacticExpansion (GLEX)

## Текущее состояние

**Дата обновления:** 03.02.2026  
**Фаза:** Phase 3 Domain — завершена, runtime-логика и тесты стабильны, готово к Phase 4

## Главное за последние изменения

- **ColonyTickModule:** первая колония больше не создаётся в `InitializeAsync`. При пустом state и включённой экспансии мод ждёт `CmdId.Event_Playfield_Loaded` (данные `PlayfieldLoad`) для `HomePlayfield` и создаёт первую колонию только после загрузки нужного playfield. По каждому тику вызывает `ColonyManager.UpdateColonyAsync` для каждой колонии.
- **ColonyManager:** добавлен метод `EnsurePlayfieldColoniesSpawnedAsync(playfield)` — вызывается при `Event_Playfield_Loaded` и обновляет/защищает структуры всех колоний на этом playfield (Touch через `StageManager.MaintainColonyStructuresAsync`), готово к расширению для спавна виртуальных структур/юнитов.
- **SimulationEngine:** после инициализации модулей по‑прежнему перезагружает состояние, а также перезагружает `state` после каждого тика, чтобы подхватывать изменения, сделанные асинхронно по событиям (например, создание колонии по `Event_Playfield_Loaded`).
- **Версия:** в ModMain строка версии обновлена на `v1.0 Phase 3`.
- **Event_Error:** в `EmpyrionGateway.HandleEvent` при `CmdId.Event_Error` извлекается сообщение из `ErrorInfo` (в т.ч. `errorType.ToString()` для понятного кода в логах, например `EntityNotLocalToPlayfield`), запрос завершается через `SequenceManager.CompleteWithError` — исключение пробрасывается вызывающему вместо "Type mismatch".
- **PlacementResolver:** поздняя инъекция `IModApi` через `SetModApi` в `ModMain.Init` для корректного определения высоты рельефа.
- **ConfigurationLoader:** мерж дефолтных `Zirax.Stages` и `Zirax.DropShips` с ванильными префабами (BA_ConstructionSite, BA_Zirax_*, BA_MiningOutpost_Zirax_1 и т.д.), если в конфиге их нет.
- **StageManager:** префаб для посадочной структуры берётся из `_config.Zirax.DropShips` или fallback `BA_ConstructionSite`.
- **Инструкция для тестера:** `docs/manuals/Tester_Manual_Colony_Access.md` — консольные команды (tt, gm, find), как найти колонию по state.json/логам.
- **Тесты:** SimulationEngineTests ожидают минимум 2 вызова `LoadAsync` (загрузка + перезагрузка после init, дальнейшие вызовы при тиках допустимы); PlacementResolverTests — тип ответа `GlobalStructureList`; все unit/integration тесты проходят.

## Текущее качество

- Unit тесты: проходят ✅  
- Integration тесты: проходят ✅  
- Сборка: ✅ успешна

## Что ещё может потребоваться

- Если при спавне структуры игра возвращает `Event_Error` — в логах и исключениях теперь виден код ошибки (ErrorType). При необходимости проверить префаб/позицию/playfield в конфиге и окружении сервера.

## Следующие шаги

1. Phase 3.5: server testing на dedicated server (deploy → проверка логов, колоний, спавна).
2. Phase 4: Threat Director + AIM Orchestrator (по архитектурной документации).
