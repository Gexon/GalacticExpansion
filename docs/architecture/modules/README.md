# Модули GLEX

**Назначение**: входная точка для детального проектирования модулей.  
**Аудитория**: разработчики.  
**Статус**: draft.  

## Список модулей

- [`SimulationEngine.md`](SimulationEngine.md)
- [`EmpyrionGateway.md`](EmpyrionGateway.md)
- [`StateStore.md`](StateStore.md)
- [`PlacementResolver.md`](PlacementResolver.md)
- [`SpawningAndEvolution.md`](SpawningAndEvolution.md)
- [`AimOrchestrator.md`](AimOrchestrator.md)
- [`PlayerTracker.md`](PlayerTracker.md)
- [`StructureTracker.md`](StructureTracker.md)
- [`ThreatDirector.md`](ThreatDirector.md)
- [`EconomySim.md`](EconomySim.md)
- [`ExpansionPlanner.md`](ExpansionPlanner.md)

## Шаблон модуля (что обязательно описываем)

- **Ответственность** (за что отвечает модуль).
- **Входы** (события/данные/конфиг).
- **Выходы** (команды/запросы/изменения state).
- **Инварианты** (правила, которые нельзя нарушать).
- **Ошибки/edge cases**.
- **Логи** (какие события обязательно логировать).
