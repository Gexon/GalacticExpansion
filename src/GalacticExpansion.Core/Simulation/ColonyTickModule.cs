using System;
using System.Linq;
using System.Threading.Tasks;
using GalacticExpansion.Core.Placement;
using GalacticExpansion.Core.Simulation.Events;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Модуль симуляции: обновление колоний по тику.
    /// Создаёт виртуальную колонию при инициализации (если state пуст).
    /// Материализация виртуальных колоний происходит через IModApi.OnPlayfieldLoaded (обрабатывается в ModMain).
    /// </summary>
    public class ColonyTickModule : ISimulationModule
    {
        private readonly IColonyManager _colonyManager;
        private readonly IPlacementResolver _placementResolver;
        private readonly IEventBus _eventBus;
        private readonly Configuration _config;
        private readonly ILogger _logger;

        /// <inheritdoc/>
        public string ModuleName => "ColonyTickModule";

        /// <inheritdoc/>
        public int UpdatePriority => 50; // После PlayerTracker (10), StructureTracker (20); до Threat/AIM

        /// <summary>
        /// Создаёт модуль обновления колоний по тику.
        /// </summary>
        /// <param name="colonyManager">Менеджер колоний для UpdateColonyAsync, CreateColonyAsync и материализации.</param>
        /// <param name="placementResolver">Резолвер размещения для поиска позиции первой колонии.</param>
        /// <param name="eventBus">Внутренний EventBus для подписки на события (зарезервировано для будущего использования).</param>
        /// <param name="config">Конфигурация (HomePlayfield, EnableExpansion, Zirax.FactionId).</param>
        /// <param name="logger">Логгер.</param>
        public ColonyTickModule(
            IColonyManager colonyManager,
            IPlacementResolver placementResolver,
            IEventBus eventBus,
            Configuration config,
            ILogger logger)
        {
            _colonyManager = colonyManager ?? throw new ArgumentNullException(nameof(colonyManager));
            _placementResolver = placementResolver ?? throw new ArgumentNullException(nameof(placementResolver));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public async Task InitializeAsync(SimulationState state)
        {
            _logger.Info("ColonyTickModule initializing...");

            // Создаём виртуальную колонию при инициализации, если state пуст и экспансия включена
            if (state.Colonies.Count == 0 && _config.EnableExpansion)
            {
                _logger.Info("ColonyTickModule: creating initial virtual colony...");
                await CreateInitialVirtualColonyAsync();
            }
            else if (state.Colonies.Count > 0)
            {
                _logger.Info($"ColonyTickModule: {state.Colonies.Count} colony(ies) already in state");
            }

            // Примечание: обработка загрузки playfield теперь происходит через IModApi.OnPlayfieldLoaded в ModMain
            // (это правильный способ детектировать готовность playfield для операций спавна)

            _logger.Info("ColonyTickModule initialized");
        }

        /// <summary>
        /// Создаёт первую виртуальную колонию на HomePlayfield.
        /// Виртуальная колония не имеет физических структур, но развивается в БД.
        /// Материализация (спавн структур) произойдёт при загрузке playfield.
        /// </summary>
        private async Task CreateInitialVirtualColonyAsync()
        {
            var homePlayfield = _config.HomePlayfield ?? "Temperate Planet";
            var factionId = _config.Zirax?.FactionId ?? 2;
            
            // Создаём виртуальную колонию с нулевой позицией (позиция будет определена при материализации)
            var virtualPosition = new Vector3(0, 0, 0);

            try
            {
                var colony = await _colonyManager.CreateColonyAsync(homePlayfield, virtualPosition, factionId, isVirtual: true);
                _logger.Info($"ColonyTickModule: created initial VIRTUAL colony {colony.Id} on '{homePlayfield}'. Will be materialized when playfield loads.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"ColonyTickModule: failed to create initial virtual colony on '{homePlayfield}'");
            }
        }


        /// <inheritdoc/>
        public void OnSimulationUpdate(SimulationContext context)
        {
            // 1. Попытка материализации отложенных колоний (retry-логика)
            try
            {
                _colonyManager.TryMaterializePendingColoniesAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "ColonyTickModule: error in TryMaterializePendingColonies");
            }

            // 2. Обновление колоний
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
