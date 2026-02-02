using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Gateway;
using GalacticExpansion.Core.Placement;
using GalacticExpansion.Core.Tracking;
using GalacticExpansion.Models;
using Moq;
using NLog;
using Xunit;

namespace GalacticExpansion.Tests.Unit.Placement
{
    /// <summary>
    /// Unit-тесты для PlacementResolver - модуля поиска мест для размещения структур.
    /// Тестирование:
    /// - Спирального алгоритма поиска
    /// - Проверки дистанций от игроков и структур
    /// - Точного определения высоты рельефа (GetTerrainHeightAt)
    /// - Обработки ошибок и timeout
    /// </summary>
    public class PlacementResolverTests
    {
        private readonly Mock<IEmpyrionGateway> _gatewayMock;
        private readonly Mock<IPlayerTracker> _playerTrackerMock;
        private readonly Mock<IApplication> _applicationMock;
        private readonly Mock<IPlayfield> _playfieldMock;
        private readonly Mock<ILogger> _loggerMock;
        private readonly PlacementResolver _resolver;

        public PlacementResolverTests()
        {
            _gatewayMock = new Mock<IEmpyrionGateway>();
            _playerTrackerMock = new Mock<IPlayerTracker>();
            _applicationMock = new Mock<IApplication>();
            _playfieldMock = new Mock<IPlayfield>();
            _loggerMock = new Mock<ILogger>();

            // По умолчанию playfield доступен
            _applicationMock.Setup(a => a.GetPlayfield(It.IsAny<string>())).Returns(_playfieldMock.Object);

            // По умолчанию высота рельефа = 100м
            _playfieldMock.Setup(p => p.GetTerrainHeightAt(It.IsAny<float>(), It.IsAny<float>())).Returns(100f);

            // По умолчанию нет структур
            _gatewayMock
                .Setup(g => g.SendRequestAsync<Dictionary<string, List<GlobalStructureInfo>>>(
                    CmdId.Request_GlobalStructure_List,
                    null,
                    It.IsAny<int>(),
                    It.IsAny<int>()))
                .ReturnsAsync(new Dictionary<string, List<GlobalStructureInfo>>
                {
                    ["Akua"] = new List<GlobalStructureInfo>()
                });

            // По умолчанию нет игроков
            _playerTrackerMock.Setup(pt => pt.GetPlayersOnPlayfield(It.IsAny<string>()))
                .Returns(new List<TrackedPlayerInfo>());

            _resolver = new PlacementResolver(_gatewayMock.Object, _playerTrackerMock.Object, _applicationMock.Object, _loggerMock.Object);
        }

        [Fact(DisplayName = "Конструктор - выбрасывает ArgumentNullException при null параметрах")]
        public void Constructor_ThrowsArgumentNullException_WhenParametersAreNull()
        {
            // Assert
            Assert.Throws<ArgumentNullException>(() => new PlacementResolver(null!, _playerTrackerMock.Object, _applicationMock.Object, _loggerMock.Object));
            Assert.Throws<ArgumentNullException>(() => new PlacementResolver(_gatewayMock.Object, null!, _applicationMock.Object, _loggerMock.Object));
            Assert.Throws<ArgumentNullException>(() => new PlacementResolver(_gatewayMock.Object, _playerTrackerMock.Object, null!, _loggerMock.Object));
            Assert.Throws<ArgumentNullException>(() => new PlacementResolver(_gatewayMock.Object, _playerTrackerMock.Object, _applicationMock.Object, null!));
        }

        [Fact(DisplayName = "FindSuitableLocationAsync - находит место когда нет препятствий")]
        public async Task FindSuitableLocationAsync_FindsLocation_WhenNoObstacles()
        {
            // Arrange
            var criteria = new PlacementCriteria
            {
                Playfield = "Akua",
                MinDistanceFromPlayers = 500f,
                MinDistanceFromPlayerStructures = 1000f,
                SearchRadius = 2000f,
                UseTerrainHeight = true,
                HeightOffset = 0.5f
            };

            // Act
            var position = await _resolver.FindSuitableLocationAsync(criteria);

            // Assert
            Assert.NotNull(position);
            Assert.Equal(100.5f, position.Y); // terrainHeight (100) + heightOffset (0.5)
        }

        [Fact(DisplayName = "FindSuitableLocationAsync - учитывает PreferredLocation как центр поиска")]
        public async Task FindSuitableLocationAsync_UsesPreferredLocation_AsSearchCenter()
        {
            // Arrange
            var preferredLocation = new Vector3(1000, 50, -500);
            var criteria = new PlacementCriteria
            {
                Playfield = "Akua",
                PreferredLocation = preferredLocation,
                SearchRadius = 100f // Маленький радиус для быстрого теста
            };

            // Act
            var position = await _resolver.FindSuitableLocationAsync(criteria);

            // Assert
            Assert.NotNull(position);
            // Позиция должна быть близко к preferredLocation (в пределах SearchRadius)
            var distance = Vector3.Distance(
                new Vector3(position.X, preferredLocation.Y, position.Z),
                preferredLocation
            );
            Assert.True(distance <= criteria.SearchRadius, $"Distance {distance} exceeds SearchRadius {criteria.SearchRadius}");
        }

        [Fact(DisplayName = "FindSuitableLocationAsync - избегает мест рядом с игроками")]
        public async Task FindSuitableLocationAsync_AvoidsPlayerPositions()
        {
            // Arrange
            var playerPosition = new Vector3(100, 100, 100);
            _playerTrackerMock.Setup(pt => pt.GetPlayersOnPlayfield("Akua"))
                .Returns(new List<TrackedPlayerInfo>
                {
                    new TrackedPlayerInfo
                    {
                        PlayerName = "TestPlayer",
                        Position = playerPosition,
                        Playfield = "Akua"
                    }
                });

            var criteria = new PlacementCriteria
            {
                Playfield = "Akua",
                MinDistanceFromPlayers = 500f, // Игрок должен быть минимум в 500м
                SearchRadius = 2000f,
                PreferredLocation = playerPosition // Начинаем поиск от позиции игрока
            };

            // Act
            var position = await _resolver.FindSuitableLocationAsync(criteria);

            // Assert
            var distance = Vector3.Distance(position, playerPosition);
            Assert.True(distance >= criteria.MinDistanceFromPlayers,
                $"Position {position} too close to player (distance={distance}, required={criteria.MinDistanceFromPlayers})");
        }

        [Fact(DisplayName = "FindSuitableLocationAsync - избегает мест рядом со структурами игроков")]
        public async Task FindSuitableLocationAsync_AvoidsPlayerStructures()
        {
            // Arrange
            var structurePosition = new PVector3(200, 100, 200);
            _gatewayMock
                .Setup(g => g.SendRequestAsync<Dictionary<string, List<GlobalStructureInfo>>>(
                    CmdId.Request_GlobalStructure_List,
                    null,
                    It.IsAny<int>(),
                    It.IsAny<int>()))
                .ReturnsAsync(new Dictionary<string, List<GlobalStructureInfo>>
                {
                    ["Akua"] = new List<GlobalStructureInfo>
                    {
                        new GlobalStructureInfo
                        {
                            id = 100,
                            factionId = 1, // Не Zirax (2)
                            pos = structurePosition,
                            playfield = "Akua"
                        }
                    }
                });

            var criteria = new PlacementCriteria
            {
                Playfield = "Akua",
                MinDistanceFromPlayerStructures = 1000f,
                SearchRadius = 2000f,
                FactionId = 2, // Zirax
                PreferredLocation = structurePosition.ToVector3()
            };

            // Act
            var position = await _resolver.FindSuitableLocationAsync(criteria);

            // Assert
            var distance = Vector3.Distance(position, structurePosition.ToVector3());
            Assert.True(distance >= criteria.MinDistanceFromPlayerStructures,
                $"Position too close to player structure (distance={distance}, required={criteria.MinDistanceFromPlayerStructures})");
        }

        [Fact(DisplayName = "FindSuitableLocationAsync - игнорирует структуры своей фракции")]
        public async Task FindSuitableLocationAsync_IgnoresOwnFactionStructures()
        {
            // Arrange
            var ziraxStructurePosition = new PVector3(50, 100, 50);
            _gatewayMock
                .Setup(g => g.SendRequestAsync<Dictionary<string, List<GlobalStructureInfo>>>(
                    CmdId.Request_GlobalStructure_List,
                    null,
                    It.IsAny<int>(),
                    It.IsAny<int>()))
                .ReturnsAsync(new Dictionary<string, List<GlobalStructureInfo>>
                {
                    ["Akua"] = new List<GlobalStructureInfo>
                    {
                        new GlobalStructureInfo
                        {
                            id = 200,
                            factionId = 2, // Zirax
                            pos = ziraxStructurePosition,
                            playfield = "Akua"
                        }
                    }
                });

            var criteria = new PlacementCriteria
            {
                Playfield = "Akua",
                MinDistanceFromPlayerStructures = 1000f,
                FactionId = 2, // Zirax
                SearchRadius = 100f,
                PreferredLocation = ziraxStructurePosition.ToVector3()
            };

            // Act
            var position = await _resolver.FindSuitableLocationAsync(criteria);

            // Assert - должно найти место рядом со своей структурой (игнорируя её)
            Assert.NotNull(position);
            var distance = Vector3.Distance(position, ziraxStructurePosition.ToVector3());
            Assert.True(distance < criteria.MinDistanceFromPlayerStructures,
                "Own faction structures should be ignored");
        }

        [Fact(DisplayName = "FindSuitableLocationAsync - выбрасывает PlacementException когда timeout")]
        public async Task FindSuitableLocationAsync_ThrowsPlacementException_WhenTimeout()
        {
            // Arrange
            var criteria = new PlacementCriteria
            {
                Playfield = "Akua",
                SearchRadius = 50000f, // Огромный радиус
                MaxSearchTimeSeconds = 1 // Timeout 1 секунда
            };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<PlacementException>(
                () => _resolver.FindSuitableLocationAsync(criteria)
            );

            Assert.Contains("timeout", exception.Message.ToLower());
        }

        [Fact(DisplayName = "FindSuitableLocationAsync - выбрасывает PlacementException когда не найдено место")]
        public async Task FindSuitableLocationAsync_ThrowsPlacementException_WhenNoSuitableLocation()
        {
            // Arrange - блокируем все места игроком
            _playerTrackerMock.Setup(pt => pt.GetPlayersOnPlayfield("Akua"))
                .Returns(new List<TrackedPlayerInfo>
                {
                    new TrackedPlayerInfo
                    {
                        PlayerName = "Blocker",
                        Position = Vector3.Zero,
                        Playfield = "Akua"
                    }
                });

            var criteria = new PlacementCriteria
            {
                Playfield = "Akua",
                MinDistanceFromPlayers = 10000f, // Огромная дистанция
                SearchRadius = 100f, // Маленький радиус поиска
                PreferredLocation = Vector3.Zero
            };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<PlacementException>(
                () => _resolver.FindSuitableLocationAsync(criteria)
            );

            Assert.Contains("No suitable location", exception.Message);
            Assert.Equal("Akua", exception.Playfield);
        }

        [Fact(DisplayName = "GetTerrainHeight - возвращает корректную высоту рельефа")]
        public void GetTerrainHeight_ReturnsCorrectHeight()
        {
            // Arrange
            _playfieldMock.Setup(p => p.GetTerrainHeightAt(100f, 200f)).Returns(123.45f);

            // Act
            var height = _resolver.GetTerrainHeight(_playfieldMock.Object, 100f, 200f);

            // Assert
            Assert.Equal(123.45f, height);
        }

        [Fact(DisplayName = "GetTerrainHeight - выбрасывает ArgumentNullException для null playfield")]
        public void GetTerrainHeight_ThrowsArgumentNullException_WhenPlayfieldIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _resolver.GetTerrainHeight(null!, 0, 0));
        }

        [Fact(DisplayName = "FindLocationAtTerrainAsync - возвращает позицию с корректной высотой")]
        public async Task FindLocationAtTerrainAsync_ReturnsPositionWithCorrectHeight()
        {
            // Arrange
            _playfieldMock.Setup(p => p.GetTerrainHeightAt(500f, -300f)).Returns(85f);

            // Act
            var position = await _resolver.FindLocationAtTerrainAsync("Akua", 500f, -300f, heightOffset: 2.0f);

            // Assert
            Assert.Equal(500f, position.X);
            Assert.Equal(87f, position.Y); // 85 (terrain) + 2 (offset)
            Assert.Equal(-300f, position.Z);
        }

        [Fact(DisplayName = "FindLocationAtTerrainAsync - выбрасывает ArgumentException для пустого playfield")]
        public async Task FindLocationAtTerrainAsync_ThrowsArgumentException_WhenPlayfieldNameIsEmpty()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                () => _resolver.FindLocationAtTerrainAsync("", 0, 0)
            );
        }

        [Fact(DisplayName = "FindLocationAtTerrainAsync - выбрасывает ArgumentException когда playfield не найден")]
        public async Task FindLocationAtTerrainAsync_ThrowsArgumentException_WhenPlayfieldNotFound()
        {
            // Arrange
            _applicationMock.Setup(a => a.GetPlayfield("NonExistent")).Returns((IPlayfield?)null);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => _resolver.FindLocationAtTerrainAsync("NonExistent", 0, 0)
            );

            Assert.Contains("not found", exception.Message.ToLower());
        }

        [Fact(DisplayName = "IsLocationSuitableAsync - возвращает true для подходящей позиции")]
        public async Task IsLocationSuitableAsync_ReturnsTrue_ForSuitablePosition()
        {
            // Arrange
            var criteria = new PlacementCriteria
            {
                Playfield = "Akua",
                MinDistanceFromPlayers = 100f,
                MinDistanceFromPlayerStructures = 200f
            };

            var position = new Vector3(5000, 100, 5000); // Далеко от всех

            // Act
            var result = await _resolver.IsLocationSuitableAsync(position, criteria);

            // Assert
            Assert.True(result);
        }

        [Fact(DisplayName = "IsLocationSuitableAsync - возвращает false когда слишком близко к игроку")]
        public async Task IsLocationSuitableAsync_ReturnsFalse_WhenTooCloseToPlayer()
        {
            // Arrange
            _playerTrackerMock.Setup(pt => pt.GetPlayersOnPlayfield("Akua"))
                .Returns(new List<TrackedPlayerInfo>
                {
                    new TrackedPlayerInfo
                    {
                        PlayerName = "TestPlayer",
                        Position = new Vector3(100, 100, 100),
                        Playfield = "Akua"
                    }
                });

            var criteria = new PlacementCriteria
            {
                Playfield = "Akua",
                MinDistanceFromPlayers = 500f
            };

            var position = new Vector3(110, 100, 110); // Близко к игроку (~14м)

            // Act
            var result = await _resolver.IsLocationSuitableAsync(position, criteria);

            // Assert
            Assert.False(result);
        }
    }
}
