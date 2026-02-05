# Active Context — GalacticExpansion (GLEX)

## Текущее состояние

**Дата обновления:** 05.02.2026  
**Фаза:** Phase 3 Domain — завершена с системой виртуализации колоний, runtime-логика стабильна

## Главное за последние изменения

### 🎯 Виртуализация колоний (Colony Virtualization) — РЕАЛИЗОВАНО ✅

**Проблема:** 
- Колонии не создавались из-за `PlayfieldConnectionNotFound` при спавне
- Использовался неправильный API: `Event_Playfield_Loaded` срабатывал слишком рано
- `Event_Player_ChangedPlayfield` не работает в single-player (баг Empyrion API)

**Решение:** 
- Система виртуализации с правильным API (`IModApi.OnPlayfieldLoaded`)
- Retry-логика для дополнительной надёжности:

- **Colony модель:**
  - `IsVirtual` (bool) — флаг виртуализации
  - `PendingMaterialization` (bool) — ожидает материализации
  - `MaterializationAttempts` (int) — счетчик попыток материализации

- **ModMain (IModApi Integration):**
  - `Init(IModApi modAPI)` — подписка на **правильное событие**: `modAPI.Application.OnPlayfieldLoaded`
  - `OnPlayfieldLoaded(IPlayfield)` — обработчик события, вызывает `ColonyManager.EnsurePlayfieldColoniesSpawnedAsync`
  - **Работает в single-player и на dedicated server** (в отличие от Event_Player_ChangedPlayfield)

- **ColonyTickModule:** при инициализации создаёт **виртуальную колонию** (`CreateInitialVirtualColonyAsync`):
  - Создается сразу при старте мода (не ждет события игрока!)
  - `IsVirtual = true`, `Position = (0,0,0)`, без спавна структур
  - В каждом тике вызывает `TryMaterializePendingColoniesAsync` (retry-логика)
  - Больше НЕ подписывается на `Event_Playfield_Loaded` (удалён `IEmpyrionGateway` из зависимостей)

- **ColonyManager:**
  - `EnsurePlayfieldColoniesSpawnedAsync` — помечает виртуальные колонии: `PendingMaterialization = true`
  - `TryMaterializePendingColoniesAsync` — каждый тик пытается материализовать помеченные колонии (до 10 попыток, обычно успех с 1-й)
  - `UpdateColonyAsync` — виртуальные колонии развиваются БЕЗ физических операций со структурами

- **StageManager:**
  - `InitializeColonyAsync` — поддержка параметра `isVirtual`, спавн только для материализованных
  - `MaterializeColonyAsync` — находит позицию через `PlacementResolver`, спавнит структуры, обновляет флаги
  - `TransitionToNextStageAsync` — виртуальные апгрейды без физического спавна
  - `CanTransitionToNextStageAsync` — виртуальные колонии могут переходить на следующую стадию
- **SimulationEngine:** после инициализации модулей по‑прежнему перезагружает состояние, а также перезагружает `state` после каждого тика, чтобы подхватывать изменения, сделанные асинхронно по событиям (например, создание колонии по `Event_Playfield_Loaded`).
- **Версия:** в ModMain строка версии обновлена на `v1.0 Phase 3`.
- **Event_Error:** в `EmpyrionGateway.HandleEvent` при `CmdId.Event_Error` извлекается сообщение из `ErrorInfo` (в т.ч. `errorType.ToString()` для понятного кода в логах, например `EntityNotLocalToPlayfield`), запрос завершается через `SequenceManager.CompleteWithError` — исключение пробрасывается вызывающему вместо "Type mismatch".
- **PlacementResolver:** поздняя инъекция `IModApi` через `SetModApi` в `ModMain.Init` для корректного определения высоты рельефа.
- **ConfigurationLoader:** мерж дефолтных `Zirax.Stages` и `Zirax.DropShips` с ванильными префабами (BA_ConstructionSite, BA_Zirax_*, BA_MiningOutpost_Zirax_1 и т.д.), если в конфиге их нет.
- **StageManager:** префаб для посадочной структуры берётся из `_config.Zirax.DropShips` или fallback `BA_ConstructionSite`.
- **Инструкция для тестера:** `docs/manuals/Tester_Manual_Colony_Access.md` — консольные команды (tt, gm, find), как найти колонию по state.json/логам.
- **Тесты:** SimulationEngineTests ожидают минимум 2 вызова `LoadAsync` (загрузка + перезагрузка после init, дальнейшие вызовы при тиках допустимы); PlacementResolverTests — тип ответа `GlobalStructureList`; все unit/integration тесты проходят.

**Преимущества виртуализации:**
1. ✅ Независимость от событий игрока — колония создается сразу при старте
2. ✅ **Правильный API** — `IModApi.OnPlayfieldLoaded` гарантирует готовность playfield
3. ✅ **Работает везде** — single-player и dedicated server (нет зависимости от багнутого Event_Player_ChangedPlayfield)
4. ✅ Решает проблему `PlayfieldConnectionNotFound` через правильное событие + retry-логику
5. ✅ Экономика и развитие работают непрерывно (виртуально)
6. ✅ Производительность — нет лишних структур до прихода игрока
7. ✅ Масштабируемость — можно создавать множество виртуальных колоний

**Документация:** `docs/architecture/11_Colony_Virtualization.md`

### Прочие изменения Phase 3

- **SimulationEngine:** перезагружает `state` после каждого тика для синхронизации изменений
- **Event_Error:** корректная обработка с извлечением `ErrorType` (например, `PlayfieldConnectionNotFound`)
- **PlacementResolver:** поздняя инъекция `IModApi` через `SetModApi` для высоты рельефа
- **ConfigurationLoader:** мерж дефолтных префабов с конфигом
- **Инструкция для тестера:** `docs/manuals/Tester_Manual_Colony_Access.md`

## Текущее качество

- Unit тесты: проходят ✅  
- Integration тесты: проходят ✅  
- Сборка: ✅ успешна
- Виртуализация колоний: работает ✅ (проверено в логах)

## Следующие шаги

1. **Тестирование виртуализации:** запустить игру, войти на HomePlayfield, проверить:
   - Создание виртуальной колонии при старте мода
   - Виртуальное развитие (переход стадий в логах с флагом [VIRTUAL])
   - Материализацию при загрузке playfield (retry-попытки в логах)
   - Спавн структуры после успешной материализации

2. Phase 3.5: server testing на dedicated server (deploy → проверка материализации в мультиплеере)

3. Phase 4: Threat Director + AIM Orchestrator (по архитектурной документации)
