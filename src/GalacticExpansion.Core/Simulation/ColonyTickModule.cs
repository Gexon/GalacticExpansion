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
    /// Первую колонию создаёт по Event_Playfield_Loaded для HomePlayfield (без спама спавна в незагруженный playfield).
    /// При загрузке playfield вызывается EnsurePlayfieldColoniesSpawnedAsync — обновление/защита структур и в будущем спавн юнитов.
    /// </summary>
    public class ColonyTickModule : ISimulationModule
    {
        private readonly IEmpyrionGateway _gateway;
        private readonly IColonyManager _colonyManager;
        private readonly IPlacementResolver _placementResolver;
        private readonly IEventBus _eventBus;
        private readonly Configuration _config;
        private readonly ILogger _logger;

        /// <summary>Флаг: первая колония ещё не создана, ждём Event_Playfield_Loaded для HomePlayfield.</summary>
        private bool _initialColonyPending;

        /// <inheritdoc/>
        public string ModuleName => "ColonyTickModule";

        /// <inheritdoc/>
        public int UpdatePriority => 50; // После PlayerTracker (10), StructureTracker (20); до Threat/AIM

        /// <summary>
        /// Создаёт модуль обновления колоний по тику.
        /// </summary>
        /// <param name="gateway">Шлюз для подписки на Event_Playfield_Loaded.</param>
        /// <param name="colonyManager">Менеджер колоний для UpdateColonyAsync, CreateColonyAsync и EnsurePlayfieldColoniesSpawnedAsync.</param>
        /// <param name="placementResolver">Резолвер размещения для поиска позиции первой колонии.</param>
        /// <param name="eventBus">Внутренний EventBus для подписки на события входа игрока на playfield.</param>
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
        public Task InitializeAsync(SimulationState state)
        {
            _logger.Info("ColonyTickModule initializing...");

            // Первую колонию больше не создаём по Event_Playfield_Loaded: спавн переносится на событие входа игрока на HomePlayfield.
            // Здесь лишь помечаем, что колония ожидается, если state пуст и экспансия включена.
            if (state.Colonies.Count == 0 && _config.EnableExpansion)
            {
                _initialColonyPending = true;
                _logger.Info("ColonyTickModule: initial colony will be created when a player enters HomePlayfield (PlayerEnteredPlayfieldEvent)");
            }
            else if (state.Colonies.Count > 0)
            {
                _logger.Info($"ColonyTickModule: {state.Colonies.Count} colony(ies) already in state, skipping initial colony creation");
            }

            // Подписка на низкоуровневые игровые события (в т.ч. Event_Playfield_Loaded) через шлюз.
            _gateway.GameEventReceived += OnGameEvent;

            // Подписка на доменное событие входа игрока на playfield.
            // Именно это событие теперь запускает создание первой колонии на HomePlayfield.
            _eventBus.Subscribe<PlayerEnteredPlayfieldEvent>(OnPlayerEnteredPlayfield);

            _logger.Info("ColonyTickModule initialized");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Обработчик событий от игры: Event_Playfield_Loaded — создание первой колонии при загрузке HomePlayfield и обновление структур на playfield.
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
            _logger.Info($"ColonyTickModule: Playfield_Loaded '{playfield}' — ensuring colonies on playfield");

            // При загрузке playfield — обновление/защита структур колоний на нём (Touch);
            // ВАЖНО: спавн первой колонии больше не привязан к этому событию, а запускается по входу игрока.
            _ = Task.Run(() => _colonyManager.EnsurePlayfieldColoniesSpawnedAsync(playfield));
        }

        /// <summary>
        /// Создаёт первую колонию на HomePlayfield.
        /// Вызывается асинхронно по доменному событию входа игрока на нужный playfield
        /// (PlayerEnteredPlayfieldEvent), после чего фактический спавн структуры идёт через StageManager/EntitySpawner.
        /// </summary>
        private async Task CreateInitialColonyWhenPlayfieldLoadedAsync(string playfield)
        {
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
                _initialColonyPending = true; // Повторить при следующем входе игрока на HomePlayfield
            }
        }

        /// <summary>
        /// Обработчик доменного события входа игрока на playfield.
        /// Спавн первой колонии переносится на это событие, чтобы гарантировать, что playfield полностью готов к операциям ModAPI.
        /// </summary>
        private void OnPlayerEnteredPlayfield(PlayerEnteredPlayfieldEvent evt)
        {
            if (evt == null)
                return;

            var homePlayfield = _config.HomePlayfield ?? "Akua";

            // Игнорируем, если экспансия выключена или первая колония уже создана/в процессе создания.
            if (!_config.EnableExpansion || !_initialColonyPending)
                return;

            // Нас интересует только вход на HomePlayfield.
            if (!string.Equals(evt.Playfield, homePlayfield, StringComparison.OrdinalIgnoreCase))
                return;

            _logger.Info(
                $"ColonyTickModule: player '{evt.PlayerName}' (Id={evt.PlayerId}) entered HomePlayfield '{evt.Playfield}' — scheduling initial colony creation");

            // Сбрасываем флаг ожидания, чтобы избежать параллельных запусков.
            _initialColonyPending = false;

            // Запускаем асинхронное создание первой колонии.
            _ = Task.Run(() => CreateInitialColonyWhenPlayfieldLoadedAsync(evt.Playfield));
        }

        /// <summary>
        /// Извлекает имя playfield из данных события Event_Playfield_Loaded.
        /// Официальный тип данных: PlayfieldLoad (Eleon.Modding) с полем playfield (string).
        /// Также поддерживает строку напрямую и объекты с свойствами Playfield/playfield/Name для совместимости.
        /// </summary>
        private string? GetPlayfieldNameFromEventData(object? data)
        {
            if (data == null) return null;

            // Официальный тип: Eleon.Modding.PlayfieldLoad с полем playfield
            var type = data.GetType();
            _logger.Debug($"GetPlayfieldNameFromEventData. type.Name: {type.Name}");
            _logger.Debug($"GetPlayfieldNameFromEventData. type.FullName : {type.FullName}");
           
            if (data is PlayfieldLoad pf)
            {
                _logger.Debug(
                    $"GetPlayfieldNameFromEventData: PlayfieldLoad " +
                    $"sec={pf.sec}, playfield='{pf.playfield}', processId={pf.processId}");

                if (!string.IsNullOrWhiteSpace(pf.playfield))
                    return pf.playfield.Trim();

                return null;
            }

            try
            {
                // Fallback: строка напрямую
                if (data is string s && !string.IsNullOrWhiteSpace(s)) return s.Trim();

                // Fallback: другие объекты с свойствами Playfield/playfield/Name
                var prop = type.GetProperty("Playfield") ?? type.GetProperty("playfield") ?? type.GetProperty("Name") ?? type.GetProperty("name");
                if (prop != null && prop.CanRead)
                {
                    var value = prop.GetValue(data);
                    if (value is string str && !string.IsNullOrWhiteSpace(str)) return str.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"GetPlayfieldNameFromEventData: Fallback error");
            }

            return null;
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
            _gateway.GameEventReceived -= OnGameEvent;
            _eventBus.Unsubscribe<PlayerEnteredPlayfieldEvent>(OnPlayerEnteredPlayfield);
            _logger.Info("ColonyTickModule shutting down");
            return Task.CompletedTask;
        }
    }
}
