using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Gateway;
using GalacticExpansion.Core.Simulation;
using GalacticExpansion.Core.Simulation.Events;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Tracking
{
    /// <summary>
    /// Отслеживает игроков на playfield'ах и публикует события входа/выхода.
    /// Кэширует информацию об игроках для быстрого доступа.
    /// </summary>
    public class PlayerTracker : IPlayerTracker
    {
        private readonly IEmpyrionGateway _gateway;
        private readonly IEventBus _eventBus;
        private readonly ILogger _logger;

        // Кэш игроков по playfield
        private readonly ConcurrentDictionary<string, ConcurrentBag<TrackedPlayerInfo>> _playersByPlayfield;
        
        // Индекс игроков по ID для быстрого доступа
        private readonly ConcurrentDictionary<int, TrackedPlayerInfo> _playersById;

        /// <inheritdoc/>
        public string ModuleName => "PlayerTracker";

        /// <inheritdoc/>
        public int UpdatePriority => 10; // Один из первых модулей

        /// <summary>
        /// Создает новый экземпляр PlayerTracker.
        /// </summary>
        /// <param name="gateway">Шлюз для взаимодействия с Empyrion</param>
        /// <param name="eventBus">Шина событий</param>
        /// <param name="logger">Логгер</param>
        public PlayerTracker(
            IEmpyrionGateway gateway,
            IEventBus eventBus,
            ILogger logger)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _playersByPlayfield = new ConcurrentDictionary<string, ConcurrentBag<TrackedPlayerInfo>>();
            _playersById = new ConcurrentDictionary<int, TrackedPlayerInfo>();

            _logger.Debug("PlayerTracker created");
        }

        /// <inheritdoc/>
        public Task InitializeAsync(SimulationState state)
        {
            _logger.Info("PlayerTracker initializing...");

            // Подписываемся на события игры
            _gateway.GameEventReceived += OnGameEvent;

            _logger.Info("PlayerTracker initialized");
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public void OnSimulationUpdate(SimulationContext context)
        {
            // PlayerTracker работает реактивно через события
            // Здесь можно добавить периодическую очистку старых данных
            _logger.Trace($"PlayerTracker: {_playersById.Count} players tracked");
        }

        /// <inheritdoc/>
        public Task ShutdownAsync()
        {
            _logger.Info("PlayerTracker shutting down...");

            // Отписываемся от событий
            _gateway.GameEventReceived -= OnGameEvent;

            // Очищаем кэш
            _playersByPlayfield.Clear();
            _playersById.Clear();

            _logger.Info("PlayerTracker shut down");
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public List<TrackedPlayerInfo> GetPlayersOnPlayfield(string playfield)
        {
            if (string.IsNullOrEmpty(playfield))
                return new List<TrackedPlayerInfo>();

            if (_playersByPlayfield.TryGetValue(playfield, out var players))
            {
                return players.ToList();
            }

            return new List<TrackedPlayerInfo>();
        }

        /// <inheritdoc/>
        public bool HasPlayersOnPlayfield(string playfield)
        {
            if (string.IsNullOrEmpty(playfield))
                return false;

            return _playersByPlayfield.TryGetValue(playfield, out var players) && players.Count > 0;
        }

        /// <inheritdoc/>
        public List<TrackedPlayerInfo> GetPlayersNearPosition(string playfield, Vector3 position, float radius)
        {
            var players = GetPlayersOnPlayfield(playfield);
            var radiusSquared = radius * radius;

            return players
                .Where(p => Vector3.DistanceSquared(p.Position, position) <= radiusSquared)
                .ToList();
        }

        /// <inheritdoc/>
        public bool IsPlayerOnline(int playerId)
        {
            return _playersById.TryGetValue(playerId, out var player) && player.IsOnline;
        }

        /// <inheritdoc/>
        public TrackedPlayerInfo? GetPlayer(int playerId)
        {
            _playersById.TryGetValue(playerId, out var player);
            return player;
        }

        /// <summary>
        /// Обработчик событий от игры.
        /// </summary>
        private void OnGameEvent(object? sender, GameEventArgs e)
        {
            try
            {
                switch (e.EventId)
                {
                    case CmdId.Event_Player_ChangedPlayfield:
                        HandlePlayerChangedPlayfield(e.Data);
                        break;

                    case CmdId.Event_Player_Connected:
                        HandlePlayerConnected(e.Data);
                        break;

                    case CmdId.Event_Player_Disconnected:
                        HandlePlayerDisconnected(e.Data);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling game event {e.EventId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Обрабатывает событие смены playfield игроком.
        /// </summary>
        private void HandlePlayerChangedPlayfield(object data)
        {
            if (!(data is IdPlayfieldPositionRotation playerData))
                return;

            var playerId = playerData.id;
            var newPlayfield = playerData.playfield;
            var position = new Vector3(playerData.pos.x, playerData.pos.y, playerData.pos.z);

            _logger.Debug($"Player {playerId} changed playfield to {newPlayfield}");

            // Получаем или создаем информацию об игроке
            var player = _playersById.GetOrAdd(playerId, id => new TrackedPlayerInfo
            {
                PlayerId = id,
                PlayerName = $"Player_{id}", // Имя будет обновлено при получении полной информации
                IsOnline = true
            });

            var oldPlayfield = player.CurrentPlayfield;

            // Удаляем игрока со старого playfield
            if (!string.IsNullOrEmpty(oldPlayfield) && oldPlayfield != newPlayfield)
            {
                RemovePlayerFromPlayfield(playerId, oldPlayfield);

                // Публикуем событие выхода
                _eventBus.Publish(new PlayerLeftPlayfieldEvent
                {
                    PlayerId = playerId,
                    PlayerName = player.PlayerName,
                    Playfield = oldPlayfield,
                    EventTime = DateTime.UtcNow
                });
            }

            // Обновляем информацию об игроке
            player.CurrentPlayfield = newPlayfield;
            player.Position = position;
            player.LastSeen = DateTime.UtcNow;
            player.IsOnline = true;

            // Добавляем игрока на новый playfield
            var playfieldPlayers = _playersByPlayfield.GetOrAdd(newPlayfield, _ => new ConcurrentBag<TrackedPlayerInfo>());
            
            // Проверяем, что игрока еще нет в списке
            if (!playfieldPlayers.Any(p => p.PlayerId == playerId))
            {
                playfieldPlayers.Add(player);
            }

            // Публикуем событие входа
            _eventBus.Publish(new PlayerEnteredPlayfieldEvent
            {
                PlayerId = playerId,
                PlayerName = player.PlayerName,
                Playfield = newPlayfield,
                EventTime = DateTime.UtcNow
            });

            _logger.Debug($"Player {playerId} tracked on {newPlayfield} at {position}");
        }

        /// <summary>
        /// Обрабатывает событие подключения игрока.
        /// </summary>
        private void HandlePlayerConnected(object data)
        {
            if (!(data is Id playerData))
                return;

            var playerId = playerData.id;

            _logger.Debug($"Player {playerId} connected");

            // Создаем или обновляем информацию об игроке
            var player = _playersById.GetOrAdd(playerId, id => new TrackedPlayerInfo
            {
                PlayerId = id,
                PlayerName = $"Player_{id}",
                IsOnline = true,
                LastSeen = DateTime.UtcNow
            });

            player.IsOnline = true;
            player.LastSeen = DateTime.UtcNow;
        }

        /// <summary>
        /// Обрабатывает событие отключения игрока.
        /// </summary>
        private void HandlePlayerDisconnected(object data)
        {
            if (!(data is Id playerData))
                return;

            var playerId = playerData.id;

            _logger.Debug($"Player {playerId} disconnected");

            if (_playersById.TryGetValue(playerId, out var player))
            {
                var oldPlayfield = player.CurrentPlayfield;

                // Удаляем игрока со всех playfield
                if (!string.IsNullOrEmpty(oldPlayfield))
                {
                    RemovePlayerFromPlayfield(playerId, oldPlayfield);

                    // Публикуем событие выхода
                    _eventBus.Publish(new PlayerLeftPlayfieldEvent
                    {
                        PlayerId = playerId,
                        PlayerName = player.PlayerName,
                        Playfield = oldPlayfield,
                        EventTime = DateTime.UtcNow
                    });
                }

                // Обновляем статус
                player.IsOnline = false;
                player.CurrentPlayfield = string.Empty;
                player.LastSeen = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Удаляет игрока с указанного playfield.
        /// </summary>
        private void RemovePlayerFromPlayfield(int playerId, string playfield)
        {
            if (_playersByPlayfield.TryGetValue(playfield, out var players))
            {
                // ConcurrentBag не поддерживает удаление, поэтому пересоздаем без удаленного игрока
                var updatedPlayers = new ConcurrentBag<TrackedPlayerInfo>(
                    players.Where(p => p.PlayerId != playerId)
                );
                _playersByPlayfield[playfield] = updatedPlayers;
            }
        }
    }
}
