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

        public async Task<bool> CanTransitionToNextStageAsync(Colony colony)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));

            // Проверка максимальной стадии
            if (colony.Stage >= ColonyStage.BaseMax)
                return false;

            // Проверка существования главной структуры
            if (colony.MainStructureId.HasValue)
            {
                var exists = await _entitySpawner.EntityExistsAsync(colony.MainStructureId.Value);
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

            _logger.Info($"Colony {colony.Id}: Transitioning from {colony.Stage} to {nextStage}");

            try
            {
                // 1. Удаление старой структуры
                if (colony.MainStructureId.HasValue)
                {
                    await _entitySpawner.DestroyEntityAsync(colony.MainStructureId.Value);
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

                // 3. Обновление состояния колонии
                colony.Stage = nextStage;
                colony.MainStructureId = newStructureId;
                colony.LastUpgradeTime = DateTime.UtcNow;

                // 4. Потребление ресурсов
                _economySimulator.ConsumeResourcesForUpgrade(colony, nextStageConfig.RequiredResources);

                // 5. Обновление ProductionRate
                colony.Resources.ProductionRate = nextStageConfig.ProductionRate;

                // 6. Пересчет capacity юнитов
                _unitEconomy.RecalculateCapacity(colony);

                // 7. Спавн охранников
                if (nextStageConfig.GuardCount > 0)
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

                // 8. Защита от decay
                await TouchStructure(newStructureId);

                // 9. Сохранение state
                var state = await _stateStore.LoadAsync();
                await _stateStore.SaveAsync(state);

                // 10. Публикация события
                _eventBus.Publish(new StageTransitionEvent
                {
                    ColonyId = colony.Id,
                    PreviousStage = colony.Stage,
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

            colony.Stage = previousStage.Value;
            colony.MainStructureId = null;

            var state = await _stateStore.LoadAsync();
            await _stateStore.SaveAsync(state);
        }

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

        public async Task<Colony> InitializeColonyAsync(string playfield, Vector3 position, int factionId)
        {
            _logger.Info($"Initializing new colony on '{playfield}' at {position}");

            var colony = new Colony(playfield, factionId, position)
            {
                Stage = ColonyStage.LandingPending
            };

            // Спавн DropShip
            var dropShipId = await _entitySpawner.SpawnStructureAtTerrainAsync(
                playfield,
                "GLEX_DropShip_T1",
                position.X,
                position.Z,
                factionId,
                heightOffset: 10f // Выше для посадки
            );

            colony.MainStructureId = dropShipId;
            colony.CreatedAt = DateTime.UtcNow;

            return colony;
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
        public string ColonyId { get; set; } = string.Empty;
        public ColonyStage PreviousStage { get; set; }
        public ColonyStage NewStage { get; set; }
    }
}
