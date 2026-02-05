using System;
using System.Linq;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Gateway;
using GalacticExpansion.Core.Placement;
using GalacticExpansion.Core.Simulation.Events;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Модуль симуляции: обновление колоний по тику и реакция на загрузку playfield.
    /// Создаёт виртуальную колонию при инициализации (если state пуст).
    /// Материализация виртуальных колоний происходит при Event_Playfield_Loaded с задержкой 3 секунды.
    /// </summary>
    public class ColonyTickModule : ISimulationModule
    {
        private readonly IEmpyrionGateway _gateway;
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
        /// <param name="gateway">Шлюз для подписки на Event_Playfield_Loaded.</param>
        /// <param name="colonyManager">Менеджер колоний для UpdateColonyAsync, CreateColonyAsync и материализации.</param>
        /// <param name="placementResolver">Резолвер размещения для поиска позиции первой колонии.</param>
        /// <param name="eventBus">Внутренний EventBus для подписки на события (зарезервировано для будущего использования).</param>
        /// <param name="config">Конфигурация (HomePlayfield, EnableExpansion, Zirax.FactionId).</param>
        /// <param name="logger">Логгер.</param>
        public ColonyTickModule(
            IEmpyrionGateway gateway,
            IColonyManager colonyManager,
            IPlacementResolver placementResolver,
            IEventBus eventBus,
            Configuration config,
            ILogger logger)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
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

            // Подписка на Event_Playfield_Loaded для материализации виртуальных колоний
            _gateway.GameEventReceived += OnGameEvent;

            _logger.Info("ColonyTickModule initialized");
        }

        /// <summary>
        /// Обработчик событий от игры: Event_Playfield_Loaded - материализация виртуальных колоний с задержкой.
        /// 
        /// AI-контекст: Event_Playfield_Loaded срабатывает когда playfield НАЧИНАЕТ загружаться,
        /// но ещё НЕ готов для операций спавна. Поэтому мы только помечаем колонии для материализации
        /// с задержкой (MaterializationAttempts = -3), а фактический спавн происходит в TryMaterializePendingColoniesAsync
        /// через 3 секунды + retry-логику.
        /// </summary>
        private void OnGameEvent(object? sender, GameEventArgs e)
        {
            if (e.EventId != CmdId.Event_Playfield_Loaded)
                return;

            var playfieldName = GetPlayfieldNameFromEventData(e.Data);
            if (string.IsNullOrWhiteSpace(playfieldName))
            {
                _logger.Debug("ColonyTickModule: Event_Playfield_Loaded received but could not get playfield name from data");
                return;
            }

            var playfield = playfieldName!;
            _logger.Info($"ColonyTickModule: Playfield_Loaded '{playfield}' - ensuring colonies on playfield");

            // Запускаем материализацию виртуальных колоний с задержкой 3 секунды
            _ = Task.Run(() => _colonyManager.EnsurePlayfieldColoniesSpawnedAsync(playfield));
        }

        /// <summary>
        /// Извлекает имя playfield из данных события Event_Playfield_Loaded.
        /// </summary>
        private string? GetPlayfieldNameFromEventData(object? data)
        {
            if (data == null) return null;

            if (data is PlayfieldLoad pf)
            {
                if (!string.IsNullOrWhiteSpace(pf.playfield))
                    return pf.playfield.Trim();
            }

            try
            {
                // Fallback: строка напрямую
                if (data is string s && !string.IsNullOrWhiteSpace(s)) return s.Trim();

                // Fallback: свойство playfield
                var type = data.GetType();
                var prop = type.GetProperty("Playfield") ?? type.GetProperty("playfield");
                if (prop != null && prop.CanRead)
                {
                    var value = prop.GetValue(data);
                    if (value is string str && !string.IsNullOrWhiteSpace(str)) return str.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "GetPlayfieldNameFromEventData: error");
            }

            return null;
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
            // ВАЖНО: TryMaterializePendingColoniesAsync теперь вызывается из ModMain.Game_Update()
            // т.к. spawn-операции работают ТОЛЬКО в контексте PfServer процесса, а SimulationEngine
            // работает в Dedi процессе (Task.Run). Вызов из Game_Update() гарантирует правильный контекст.

            // Обновление колоний
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
            _gateway.GameEventReceived -= OnGameEvent;
            _logger.Info("ColonyTickModule shutting down");
            return Task.CompletedTask;
        }
    }
}
