using System;
using System.Linq;
using System.Threading.Tasks;
using GalacticExpansion.Core.Placement;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Модуль симуляции, отвечающий за обновление колоний на каждом тике и создание первой колонии при пустом state.
    /// Вызывает ColonyManager.UpdateColonyAsync для каждой колонии и при старте (если колоний нет) создаёт начальную колонию на HomePlayfield.
    /// </summary>
    public class ColonyTickModule : ISimulationModule
    {
        private readonly IColonyManager _colonyManager;
        private readonly IPlacementResolver _placementResolver;
        private readonly Configuration _config;
        private readonly ILogger _logger;

        /// <inheritdoc/>
        public string ModuleName => "ColonyTickModule";

        /// <inheritdoc/>
        public int UpdatePriority => 50; // После PlayerTracker (10), StructureTracker (20); до Threat/AIM

        /// <summary>
        /// Создаёт модуль обновления колоний по тику.
        /// </summary>
        /// <param name="colonyManager">Менеджер колоний для UpdateColonyAsync и CreateColonyAsync.</param>
        /// <param name="placementResolver">Резолвер размещения для поиска позиции первой колонии.</param>
        /// <param name="config">Конфигурация (HomePlayfield, EnableExpansion, Zirax.FactionId).</param>
        /// <param name="logger">Логгер.</param>
        public ColonyTickModule(
            IColonyManager colonyManager,
            IPlacementResolver placementResolver,
            Configuration config,
            ILogger logger)
        {
            _colonyManager = colonyManager ?? throw new ArgumentNullException(nameof(colonyManager));
            _placementResolver = placementResolver ?? throw new ArgumentNullException(nameof(placementResolver));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public async Task InitializeAsync(SimulationState state)
        {
            _logger.Info("ColonyTickModule initializing...");

            // Создание первой колонии, если колоний нет и экспансия включена
            if (state.Colonies.Count == 0 && _config.EnableExpansion)
            {
                var playfield = _config.HomePlayfield ?? "Akua";
                var factionId = _config.Zirax?.FactionId ?? 2;

                Vector3 position;
                try
                {
                    var criteria = new PlacementCriteria
                    {
                        Playfield = playfield,
                        MinDistanceFromPlayers = _config.Placement?.MinDistanceFromPlayers ?? 500f,
                        MinDistanceFromPlayerStructures = _config.Placement?.MinDistanceFromPlayerStructures ?? 1000f,
                        SearchRadius = _config.Placement?.SearchRadius ?? 2000f,
                        FactionId = factionId
                    };
                    position = await _placementResolver.FindSuitableLocationAsync(criteria);
                    _logger.Info($"ColonyTickModule: found suitable location for initial colony at {position}");
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "ColonyTickModule: FindSuitableLocationAsync failed, using fallback position (0, 100, 0)");
                    position = new Vector3(0, 100, 0);
                }

                try
                {
                    var colony = await _colonyManager.CreateColonyAsync(playfield, position, factionId);
                    _logger.Info($"ColonyTickModule: created initial colony {colony.Id} on {playfield} at {position}");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"ColonyTickModule: failed to create initial colony on {playfield}");
                }
            }
            else if (state.Colonies.Count > 0)
            {
                _logger.Info($"ColonyTickModule: {state.Colonies.Count} colony(ies) already in state, skipping initial colony creation");
            }

            _logger.Info("ColonyTickModule initialized");
        }

        /// <inheritdoc/>
        public void OnSimulationUpdate(SimulationContext context)
        {
            if (context?.CurrentState?.Colonies == null || context.CurrentState.Colonies.Count == 0)
            {
                _logger.Debug("ColonyTickModule: no colonies to update");
                return;
            }

            var count = context.CurrentState.Colonies.Count;
            _logger.Debug($"ColonyTickModule: tick #{context.TickNumber}, updating {count} colon(ies), dt={context.DeltaTime:F2}s");

            foreach (var colony in context.CurrentState.Colonies.ToList())
            {
                try
                {
                    _colonyManager.UpdateColonyAsync(colony, context.DeltaTime).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"ColonyTickModule: error updating colony {colony.Id}");
                }
            }
        }

        /// <inheritdoc/>
        public Task ShutdownAsync()
        {
            _logger.Info("ColonyTickModule shutting down");
            return Task.CompletedTask;
        }
    }
}
