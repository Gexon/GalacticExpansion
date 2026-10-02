using System;
using System.Linq;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Economy;
using GalacticExpansion.Core.Gateway;
using GalacticExpansion.Core.Placement;
using GalacticExpansion.Core.Simulation;
using GalacticExpansion.Core.State;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Spawning
{
    /// <summary>
    /// Реализация менеджера стадий колоний.
    /// Управляет жизненным циклом: переходы, откаты, защита от decay.
    /// </summary>
    public class StageManager : IStageManager
    {
        private readonly IEmpyrionGateway _gateway;
        private readonly IEntitySpawner _entitySpawner;
        private readonly IPlacementResolver _placementResolver;
        private readonly IEconomySimulator _economySimulator;
        private readonly IUnitEconomyManager _unitEconomy;
        private readonly IStateStore _stateStore;
        private readonly IEventBus _eventBus;
        private readonly Configuration _config;
        private readonly ILogger _logger;

        /// <summary>
        /// Создаёт менеджер стадий с зависимостями: шлюз, спавнер, размещение, экономика, юнит-экономика, state, event bus, конфиг, логгер.
        /// </summary>
        /// <param name="gateway">Шлюз Empyrion API.</param>
        /// <param name="entitySpawner">Спавнер сущностей.</param>
        /// <param name="placementResolver">Резолвер размещения.</param>
        /// <param name="economySimulator">Симулятор экономики.</param>
        /// <param name="unitEconomy">Менеджер юнит-экономики.</param>
        /// <param name="stateStore">Хранилище состояния.</param>
        /// <param name="eventBus">Шина событий.</param>
        /// <param name="config">Конфигурация мода.</param>
        /// <param name="logger">Логгер.</param>
        public StageManager(
            IEmpyrionGateway gateway,
            IEntitySpawner entitySpawner,
            IPlacementResolver placementResolver,
            IEconomySimulator economySimulator,
            IUnitEconomyManager unitEconomy,
            IStateStore stateStore,
            IEventBus eventBus,
            Configuration config,
            ILogger logger)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _entitySpawner = entitySpawner ?? throw new ArgumentNullException(nameof(entitySpawner));
            _placementResolver = placementResolver ?? throw new ArgumentNullException(nameof(placementResolver));
            _economySimulator = economySimulator ?? throw new ArgumentNullException(nameof(economySimulator));
            _unitEconomy = unitEconomy ?? throw new ArgumentNullException(nameof(unitEconomy));
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Проверяет, можно ли перейти на следующую стадию (ресурсы, время, существование главной структуры).
        /// Виртуальные колонии могут переходить на следующую стадию без проверки структур.
        /// </summary>
        /// <param name="colony">Колония.</param>
        /// <returns>True, если переход возможен.</returns>
        public async Task<bool> CanTransitionToNextStageAsync(Colony colony)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));

            // Проверка максимальной стадии
            if (colony.Stage >= ColonyStage.BaseMax)
                return false;

            // Проверка существования главной структуры (ТОЛЬКО для материализованных колоний)
            if (!colony.IsVirtual && colony.MainStructureId.HasValue)
            {
                var exists = await _entitySpawner.EntityExistsAsync(colony.Playfield, colony.MainStructureId.Value);
                if (!exists)
                {
                    _logger.Warn($"Colony {colony.Id}: Main structure {colony.MainStructureId} does not exist!");
                    return false;
                }
            }

            // Проверка ресурсов
            if (!_economySimulator.HasEnoughResourcesForUpgrade(colony))
                return false;

            // Проверка минимального времени с последнего апгрейда
            var stageConfig = _config.Zirax.Stages.FirstOrDefault(s => s.Stage == colony.Stage.ToString());
            if (stageConfig != null && colony.LastUpgradeTime.HasValue)
            {
                var timeSinceUpgrade = (DateTime.UtcNow - colony.LastUpgradeTime.Value).TotalSeconds;
                if (timeSinceUpgrade < stageConfig.MinTimeSeconds)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Выполняет переход колонии на следующую стадию: замена структуры, потребление ресурсов, спавн охранников, сохранение state, публикация события.
        /// </summary>
        /// <param name="colony">Колония.</param>
        public async Task TransitionToNextStageAsync(Colony colony)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));

            var currentStage = colony.Stage;
            var nextStage = colony.Stage.GetNextStage();
            
            // Проверяем, можно ли перейти на следующую стадию
            if (currentStage == nextStage)
            {
                _logger.Warn($"Colony {colony.Id} is already at max stage {currentStage}");
                return;
            }

            var nextStageConfig = _config.Zirax.Stages.FirstOrDefault(s => s.Stage == nextStage.ToString());
            if (nextStageConfig == null)
            {
                _logger.Error($"No configuration found for stage {nextStage}");
                return;
            }

            var virtualFlag = colony.IsVirtual ? " [VIRTUAL]" : "";
            _logger.Info($"Colony {colony.Id}: Transitioning from {colony.Stage} to {nextStage}{virtualFlag}");

            try
            {
                // Физические операции ТОЛЬКО для материализованных колоний
                if (!colony.IsVirtual)
                {
                    // 1. Удаление старой структуры
                    if (colony.MainStructureId.HasValue)
                    {
                        await _entitySpawner.DestroyEntityAsync(colony.Playfield, colony.MainStructureId.Value);
                    }

                    // 2. Спавн новой структуры
                    var newStructureId = await _entitySpawner.SpawnStructureAtTerrainAsync(
                        colony.Playfield,
                        nextStageConfig.PrefabName,
                        colony.Position.X,
                        colony.Position.Z,
                        colony.FactionId,
                        heightOffset: 0.5f
                    );

                    colony.MainStructureId = newStructureId;
                    
                    // 8. Защита от decay
                    await TouchStructure(newStructureId);
                }

                // 3. Обновление состояния колонии (работает для всех)
                colony.Stage = nextStage;
                colony.LastUpgradeTime = DateTime.UtcNow;

                // 4. Потребление ресурсов (работает для всех)
                _economySimulator.ConsumeResourcesForUpgrade(colony, nextStageConfig.RequiredResources);

                // 5. Обновление ProductionRate (работает для всех)
                colony.Resources.ProductionRate = nextStageConfig.ProductionRate;

                // 6. Пересчет capacity юнитов (работает для всех)
                _unitEconomy.RecalculateCapacity(colony);

                // 7. Спавн охранников (ТОЛЬКО для материализованных)
                if (!colony.IsVirtual && nextStageConfig.GuardCount > 0)
                {
                    if (_unitEconomy.ReserveUnits(colony, UnitType.Guard, nextStageConfig.GuardCount))
                    {
                        var guardIds = await _entitySpawner.SpawnNPCGroupAsync(
                            colony.Playfield,
                            "ZiraxMinigunPatrol",
                            colony.Position,
                            nextStageConfig.GuardCount,
                            "Zirax"
                        );

                        foreach (var guardId in guardIds)
                        {
                            _unitEconomy.RegisterActiveUnit(colony, guardId, UnitType.Guard, "BaseDefense");
                        }
                    }
                }

                // 9. Сохранение: colony — уже объект из in-memory state (SimulationEngine._state.Colonies).
                // Все изменения (Stage, Resources, UnitPool) применены напрямую к нему.
                // Персистентность обеспечивается автосохранением SimulationEngine каждые 60 сек и при shutdown.

                // 10. Публикация события
                // Публикуем событие с правильной фиксацией "откуда → куда",
                // чтобы подписчики могли корректно реагировать на переходы.
                _eventBus.Publish(new StageTransitionEvent
                {
                    ColonyId = colony.Id,
                    PreviousStage = currentStage,
                    NewStage = nextStage
                });

                _logger.Info($"✅ Colony {colony.Id}: Transition to {nextStage} completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"❌ Failed to transition colony {colony.Id} to {nextStage}");
                throw;
            }
        }

        /// <summary>
        /// Понижает стадию колонии на одну ступень (обновляет state, сбрасывает MainStructureId).
        /// </summary>
        /// <param name="colony">Колония.</param>
        public async Task DowngradeColonyAsync(Colony colony)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));

            var previousStage = colony.Stage.GetPreviousStage();
            if (!previousStage.HasValue)
            {
                _logger.Warn($"Colony {colony.Id} cannot be downgraded below {colony.Stage}");
                return;
            }

            _logger.Warn($"Colony {colony.Id}: Downgrading from {colony.Stage} to {previousStage.Value}");

            // colony — уже объект из in-memory state. Обновляем напрямую.
            // Персистентность обеспечивается автосохранением SimulationEngine.
            colony.Stage = previousStage.Value;
            colony.MainStructureId = null;
        }

        /// <summary>
        /// Поддерживает структуры колонии: Touch главной структуры и аванпостов для защиты от decay.
        /// </summary>
        /// <param name="colony">Колония.</param>
        public async Task MaintainColonyStructuresAsync(Colony colony)
        {
            if (colony == null || !colony.MainStructureId.HasValue)
                return;

            await TouchStructure(colony.MainStructureId.Value);

            // Touch аванпостов
            foreach (var node in colony.ResourceNodes)
            {
                if (node.StructureId.HasValue && node.StructureId.Value > 0)
                {
                    await TouchStructure(node.StructureId.Value);
                }
            }
        }

        /// <summary>
        /// Инициализирует новую колонию: создаёт объект Colony, опционально спавнит DropShip.
        /// </summary>
        /// <param name="playfield">Название playfield.</param>
        /// <param name="position">Позиция колонии (может быть нулевой для виртуальных).</param>
        /// <param name="factionId">Идентификатор фракции.</param>
        /// <param name="isVirtual">Создать виртуальную колонию (без спавна структур).</param>
        /// <returns>Созданная колония в стадии LandingPending.</returns>
        public async Task<Colony> InitializeColonyAsync(string playfield, Vector3 position, int factionId, bool isVirtual = false)
        {
            var virtualFlag = isVirtual ? " [VIRTUAL]" : "";
            _logger.Info($"Initializing new colony on '{playfield}' at {position}{virtualFlag}");

            var colony = new Colony(playfield, factionId, position, isVirtual)
            {
                Stage = ColonyStage.LandingPending,
                CreatedAt = DateTime.UtcNow
            };

            // Логистический корабль доставляет начальные ресурсы при создании колонии.
            // Ресурсов должно хватать на ConstructionYard + BaseL1 (с 10% запасом).
            // Самостоятельное накопление через ProductionRate начинается только с BaseL2.
            var constructionYardConfig = _config.Zirax?.Stages?.FirstOrDefault(s => s.Stage == "ConstructionYard");
            var baseL1Config = _config.Zirax?.Stages?.FirstOrDefault(s => s.Stage == "BaseL1");

            float initialResources = (constructionYardConfig?.RequiredResources ?? 0)
                                   + (baseL1Config?.RequiredResources ?? 1000);
            initialResources *= 1.1f;

            colony.Resources.VirtualResources = initialResources;
            colony.Resources.ProductionRate = constructionYardConfig?.ProductionRate ?? 100;

            _logger.Info($"Colony {colony.Id}: logistics ship delivered {initialResources:F0} resources (enough for ConstructionYard + BaseL1)");

            // Спавн структуры ТОЛЬКО для материализованных колоний
            if (!isVirtual)
            {
                // Спавн «посадочной» структуры (префаб из конфига или стандартный BA_ConstructionSite)
                var dropPrefab = _config.Zirax?.DropShips?.FirstOrDefault()?.PrefabName ?? "BA_ConstructionSite";
                var dropShipId = await _entitySpawner.SpawnStructureAtTerrainAsync(
                    playfield,
                    dropPrefab,
                    position.X,
                    position.Z,
                    factionId,
                    heightOffset: 0.5f // Module_04 §3.2: полметра над землёй, не 10 м в воздухе
                );

                colony.MainStructureId = dropShipId;
                _logger.Info($"Colony {colony.Id} initialized with structure {dropShipId}");
            }
            else
            {
                _logger.Info($"Virtual colony {colony.Id} initialized without structures");
            }

            return colony;
        }

        /// <summary>
        /// Материализует виртуальную колонию: находит подходящую позицию и спавнит структуры.
        /// Обновляет IsVirtual = false, Position, MainStructureId.
        /// </summary>
        /// <param name="colony">Виртуальная колония для материализации.</param>
        public async Task MaterializeColonyAsync(Colony colony)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));

            if (!colony.IsVirtual)
            {
                _logger.Warn($"Colony {colony.Id} is already materialized");
                return;
            }

            _logger.Info($"Materializing virtual colony {colony.Id} on '{colony.Playfield}'...");

            // Находим подходящую позицию для материализации
            Vector3 position;
            try
            {
                var criteria = new PlacementCriteria
                {
                    Playfield = colony.Playfield,
                    MinDistanceFromPlayers = _config.Placement?.MinDistanceFromPlayers ?? 500f,
                    MinDistanceFromPlayerStructures = _config.Placement?.MinDistanceFromPlayerStructures ?? 1000f,
                    SearchRadius = _config.Placement?.SearchRadius ?? 2000f,
                    FactionId = colony.FactionId
                };
                position = await _placementResolver.FindSuitableLocationAsync(criteria);
                _logger.Info($"Found suitable location for colony {colony.Id} at {position}");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"FindSuitableLocationAsync failed for colony {colony.Id}, using fallback position (0, 100, 0)");
                position = new Vector3(0, 100, 0);
            }

            // Обновляем позицию колонии
            colony.Position = position;

            // Спавним структуру текущей стадии колонии (ConstructionYard -> BA_ConstructionSite, BaseL1 -> BA_Zirax_Small_1 и т.д.)
            var stageConfig = _config.Zirax.Stages.FirstOrDefault(s => s.Stage == colony.Stage.ToString());
            var prefab = stageConfig?.PrefabName ?? "BA_ConstructionSite";
            _logger.Info($"Colony {colony.Id}: materializing with prefab '{prefab}' for stage {colony.Stage}");
            
            try
            {
                var structureId = await _entitySpawner.SpawnStructureAtTerrainAsync(
                    colony.Playfield,
                    prefab,
                    position.X,
                    position.Z,
                    colony.FactionId,
                    heightOffset: 0.5f
                );

                colony.MainStructureId = structureId;
                colony.IsVirtual = false; // Колония теперь материализована

                _logger.Info($"Colony {colony.Id} materialized successfully with structure {structureId} (prefab: {prefab}) at {position}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to materialize colony {colony.Id}");
                throw;
            }
        }

        /// <summary>
        /// Защита структуры от авто-удаления (Touch)
        /// </summary>
        private async Task TouchStructure(int structureId)
        {
            try
            {
                await _gateway.SendRequestAsync<object>(
                    CmdId.Request_Structure_Touch,
                    new Id { id = structureId },
                    timeoutMs: 3000
                );

                _logger.Trace($"Structure {structureId} touched");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to touch structure {structureId}");
            }
        }
    }

    /// <summary>
    /// Событие перехода колонии между стадиями
    /// </summary>
    public class StageTransitionEvent
    {
        /// <summary>Идентификатор колонии.</summary>
        public string ColonyId { get; set; } = string.Empty;
        /// <summary>Предыдущая стадия.</summary>
        public ColonyStage PreviousStage { get; set; }
        /// <summary>Новая стадия после перехода.</summary>
        public ColonyStage NewStage { get; set; }
    }
}
