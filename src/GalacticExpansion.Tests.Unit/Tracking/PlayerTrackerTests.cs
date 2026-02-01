using System;
using System.Linq;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Gateway;
using GalacticExpansion.Core.Simulation;
using GalacticExpansion.Core.Simulation.Events;
using GalacticExpansion.Core.Tracking;
using GalacticExpansion.Models;
using Moq;
using NLog;
using Xunit;

namespace GalacticExpansion.Tests.Unit.Tracking
{
    /// <summary>
    /// Unit-тесты для PlayerTracker.
    /// Проверяют корректность отслеживания игроков и публикации событий.
    /// </summary>
    public class PlayerTrackerTests
    {
        private readonly Mock<IEmpyrionGateway> _mockGateway;
        private readonly Mock<IEventBus> _mockEventBus;
        private readonly Mock<ILogger> _mockLogger;
        private readonly PlayerTracker _tracker;

        public PlayerTrackerTests()
        {
            _mockGateway = new Mock<IEmpyrionGateway>();
            _mockEventBus = new Mock<IEventBus>();
            _mockLogger = new Mock<ILogger>();

            _tracker = new PlayerTracker(
                _mockGateway.Object,
                _mockEventBus.Object,
                _mockLogger.Object
            );
        }

        [Fact]
        public async Task InitializeAsync_ShouldSubscribeToGatewayEvents()
        {
            // Arrange
            var state = new SimulationState();

            // Act
            await _tracker.InitializeAsync(state);

            // Assert
            _mockGateway.VerifyAdd(g => g.GameEventReceived += It.IsAny<EventHandler<GameEventArgs>>(), Times.Once);
        }

        [Fact]
        public async Task ShutdownAsync_ShouldUnsubscribeFromGatewayEvents()
        {
            // Arrange
            var state = new SimulationState();
            await _tracker.InitializeAsync(state);

            // Act
            await _tracker.ShutdownAsync();

            // Assert
            _mockGateway.VerifyRemove(g => g.GameEventReceived -= It.IsAny<EventHandler<GameEventArgs>>(), Times.Once);
        }

        [Fact]
        public void GetPlayersOnPlayfield_WithNoPlayers_ShouldReturnEmptyList()
        {
            // Act
            var players = _tracker.GetPlayersOnPlayfield("TestPlayfield");

            // Assert
            Assert.Empty(players);
        }

        [Fact]
        public void HasPlayersOnPlayfield_WithNoPlayers_ShouldReturnFalse()
        {
            // Act
            var hasPlayers = _tracker.HasPlayersOnPlayfield("TestPlayfield");

            // Assert
            Assert.False(hasPlayers);
        }

        [Fact]
        public void IsPlayerOnline_WithUnknownPlayer_ShouldReturnFalse()
        {
            // Act
            var isOnline = _tracker.IsPlayerOnline(12345);

            // Assert
            Assert.False(isOnline);
        }

        [Fact]
        public void GetPlayer_WithUnknownPlayer_ShouldReturnNull()
        {
            // Act
            var player = _tracker.GetPlayer(12345);

            // Assert
            Assert.Null(player);
        }

        [Fact]
        public async Task PlayerChangedPlayfield_ShouldTrackPlayer()
        {
            // Arrange
            await _tracker.InitializeAsync(new SimulationState());

            var eventData = new IdPlayfieldPositionRotation
            {
                id = 100,
                playfield = "Akua",
                pos = new PVector3(1000, 100, 2000),
                rot = new PVector3(0, 0, 0)
            };

            // Act
            _mockGateway.Raise(g => g.GameEventReceived += null, 
                new GameEventArgs(CmdId.Event_Player_ChangedPlayfield, 0, eventData));

            // Assert
            var players = _tracker.GetPlayersOnPlayfield("Akua");
            Assert.Single(players);
            Assert.Equal(100, players[0].PlayerId);
            Assert.Equal("Akua", players[0].CurrentPlayfield);
        }

        [Fact]
        public async Task PlayerChangedPlayfield_ShouldPublishEnteredEvent()
        {
            // Arrange
            await _tracker.InitializeAsync(new SimulationState());

            var eventData = new IdPlayfieldPositionRotation
            {
                id = 100,
                playfield = "Akua",
                pos = new PVector3(1000, 100, 2000),
                rot = new PVector3(0, 0, 0)
            };

            // Act
            _mockGateway.Raise(g => g.GameEventReceived += null, 
                new GameEventArgs(CmdId.Event_Player_ChangedPlayfield, 0, eventData));

            // Assert
            _mockEventBus.Verify(
                e => e.Publish(It.Is<PlayerEnteredPlayfieldEvent>(evt => 
                    evt.PlayerId == 100 && evt.Playfield == "Akua")),
                Times.Once
            );
        }

        [Fact]
        public async Task PlayerChangedPlayfield_FromOldPlayfield_ShouldPublishLeftEvent()
        {
            // Arrange
            await _tracker.InitializeAsync(new SimulationState());

            // First playfield change
            var firstEvent = new IdPlayfieldPositionRotation
            {
                id = 100,
                playfield = "Akua",
                pos = new PVector3(1000, 100, 2000),
                rot = new PVector3(0, 0, 0)
            };
            _mockGateway.Raise(g => g.GameEventReceived += null, 
                new GameEventArgs(CmdId.Event_Player_ChangedPlayfield, 0, firstEvent));

            // Second playfield change
            var secondEvent = new IdPlayfieldPositionRotation
            {
                id = 100,
                playfield = "Omicron",
                pos = new PVector3(2000, 200, 3000),
                rot = new PVector3(0, 0, 0)
            };

            // Act
            _mockGateway.Raise(g => g.GameEventReceived += null, 
                new GameEventArgs(CmdId.Event_Player_ChangedPlayfield, 0, secondEvent));

            // Assert - Should publish left event for Akua
            _mockEventBus.Verify(
                e => e.Publish(It.Is<PlayerLeftPlayfieldEvent>(evt => 
                    evt.PlayerId == 100 && evt.Playfield == "Akua")),
                Times.Once
            );

            // And entered event for Omicron
            _mockEventBus.Verify(
                e => e.Publish(It.Is<PlayerEnteredPlayfieldEvent>(evt => 
                    evt.PlayerId == 100 && evt.Playfield == "Omicron")),
                Times.Once
            );
        }

        [Fact]
        public async Task PlayerConnected_ShouldMarkPlayerOnline()
        {
            // Arrange
            await _tracker.InitializeAsync(new SimulationState());

            var eventData = new Id { id = 100 };

            // Act
            _mockGateway.Raise(g => g.GameEventReceived += null, 
                new GameEventArgs(CmdId.Event_Player_Connected, 0, eventData));

            // Assert
            Assert.True(_tracker.IsPlayerOnline(100));
        }

        [Fact]
        public async Task PlayerDisconnected_ShouldMarkPlayerOffline()
        {
            // Arrange
            await _tracker.InitializeAsync(new SimulationState());

            // First connect
            var connectData = new Id { id = 100 };
            _mockGateway.Raise(g => g.GameEventReceived += null, 
                new GameEventArgs(CmdId.Event_Player_Connected, 0, connectData));

            // Then disconnect
            var disconnectData = new Id { id = 100 };

            // Act
            _mockGateway.Raise(g => g.GameEventReceived += null, 
                new GameEventArgs(CmdId.Event_Player_Disconnected, 0, disconnectData));

            // Assert
            Assert.False(_tracker.IsPlayerOnline(100));
        }

        [Fact]
        public async Task GetPlayersNearPosition_ShouldReturnPlayersInRadius()
        {
            // Arrange
            await _tracker.InitializeAsync(new SimulationState());

            // Add player at (1000, 100, 2000)
            var player1Event = new IdPlayfieldPositionRotation
            {
                id = 100,
                playfield = "Akua",
                pos = new PVector3(1000, 100, 2000),
                rot = new PVector3(0, 0, 0)
            };
            _mockGateway.Raise(g => g.GameEventReceived += null, 
                new GameEventArgs(CmdId.Event_Player_ChangedPlayfield, 0, player1Event));

            // Add player at (1100, 100, 2100) - ~141m away
            var player2Event = new IdPlayfieldPositionRotation
            {
                id = 101,
                playfield = "Akua",
                pos = new PVector3(1100, 100, 2100),
                rot = new PVector3(0, 0, 0)
            };
            _mockGateway.Raise(g => g.GameEventReceived += null, 
                new GameEventArgs(CmdId.Event_Player_ChangedPlayfield, 0, player2Event));

            // Add player at (2000, 100, 3000) - ~1414m away
            var player3Event = new IdPlayfieldPositionRotation
            {
                id = 102,
                playfield = "Akua",
                pos = new PVector3(2000, 100, 3000),
                rot = new PVector3(0, 0, 0)
            };
            _mockGateway.Raise(g => g.GameEventReceived += null, 
                new GameEventArgs(CmdId.Event_Player_ChangedPlayfield, 0, player3Event));

            // Act - Search with radius 200m from (1000, 100, 2000)
            var nearbyPlayers = _tracker.GetPlayersNearPosition(
                "Akua", 
                new Vector3(1000, 100, 2000), 
                200f
            );

            // Assert - Should find players 100 and 101, but not 102
            Assert.Equal(2, nearbyPlayers.Count);
            Assert.Contains(nearbyPlayers, p => p.PlayerId == 100);
            Assert.Contains(nearbyPlayers, p => p.PlayerId == 101);
            Assert.DoesNotContain(nearbyPlayers, p => p.PlayerId == 102);
        }

        [Fact]
        public void ModuleName_ShouldReturnPlayerTracker()
        {
            // Assert
            Assert.Equal("PlayerTracker", _tracker.ModuleName);
        }

        [Fact]
        public void UpdatePriority_ShouldReturn10()
        {
            // Assert
            Assert.Equal(10, _tracker.UpdatePriority);
        }
    }
}
