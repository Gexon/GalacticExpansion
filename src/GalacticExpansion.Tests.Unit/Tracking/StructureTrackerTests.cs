using System;
using System.Collections.Generic;
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
    /// Unit-тесты для StructureTracker.
    /// Проверяют корректность отслеживания структур и детектирования изменений.
    /// </summary>
    public class StructureTrackerTests
    {
        private readonly Mock<IEmpyrionGateway> _mockGateway;
        private readonly Mock<IEventBus> _mockEventBus;
        private readonly Mock<ILogger> _mockLogger;
        private readonly StructureTracker _tracker;

        public StructureTrackerTests()
        {
            _mockGateway = new Mock<IEmpyrionGateway>();
            _mockEventBus = new Mock<IEventBus>();
            _mockLogger = new Mock<ILogger>();

            _tracker = new StructureTracker(
                _mockGateway.Object,
                _mockEventBus.Object,
                _mockLogger.Object
            );
        }

        [Fact]
        public async Task InitializeAsync_ShouldLoadStructures()
        {
            // Arrange
            var structures = CreateMockStructureList(
                (1, "Akua", EntityType.BA, 1),
                (2, "Akua", EntityType.CV, 1)
            );

            _mockGateway
                .Setup(g => g.SendRequestAsync<GlobalStructureList>(
                    CmdId.Request_GlobalStructure_List,
                    null,
                    It.IsAny<int>()))
                .ReturnsAsync(structures);

            var state = new SimulationState();

            // Act
            await _tracker.InitializeAsync(state);

            // Assert
            Assert.Equal(2, _tracker.GetStructuresOnPlayfield("Akua").Count);
        }

        [Fact]
        public async Task RefreshStructuresAsync_ShouldUpdateCache()
        {
            // Arrange
            var structures = CreateMockStructureList(
                (1, "Akua", EntityType.BA, 1)
            );

            _mockGateway
                .Setup(g => g.SendRequestAsync<GlobalStructureList>(
                    CmdId.Request_GlobalStructure_List,
                    null,
                    It.IsAny<int>()))
                .ReturnsAsync(structures);

            // Act
            await _tracker.RefreshStructuresAsync();

            // Assert
            Assert.True(_tracker.StructureExists(1));
        }

        [Fact]
        public async Task GetStructuresOnPlayfield_ShouldReturnCorrectStructures()
        {
            // Arrange
            var structures = CreateMockStructureList(
                (1, "Akua", EntityType.BA, 1),
                (2, "Akua", EntityType.CV, 1),
                (3, "Omicron", EntityType.BA, 1)
            );

            _mockGateway
                .Setup(g => g.SendRequestAsync<GlobalStructureList>(
                    CmdId.Request_GlobalStructure_List,
                    null,
                    It.IsAny<int>()))
                .ReturnsAsync(structures);

            await _tracker.RefreshStructuresAsync();

            // Act
            var akuaStructures = _tracker.GetStructuresOnPlayfield("Akua");
            var omicronStructures = _tracker.GetStructuresOnPlayfield("Omicron");

            // Assert
            Assert.Equal(2, akuaStructures.Count);
            Assert.Single(omicronStructures);
        }

        [Fact]
        public async Task GetStructuresOnPlayfield_WithFactionFilter_ShouldReturnFiltered()
        {
            // Arrange
            var structures = CreateMockStructureList(
                (1, "Akua", EntityType.BA, 1), // Faction 1
                (2, "Akua", EntityType.CV, 2), // Faction 2
                (3, "Akua", EntityType.BA, 1)  // Faction 1
            );

            _mockGateway
                .Setup(g => g.SendRequestAsync<GlobalStructureList>(
                    CmdId.Request_GlobalStructure_List,
                    null,
                    It.IsAny<int>()))
                .ReturnsAsync(structures);

            await _tracker.RefreshStructuresAsync();

            // Act
            var faction1Structures = _tracker.GetStructuresOnPlayfield("Akua", factionId: 1);

            // Assert
            Assert.Equal(2, faction1Structures.Count);
            Assert.All(faction1Structures, s => Assert.Equal(1, s.factionId));
        }

        [Fact]
        public async Task GetStructure_ShouldReturnCorrectStructure()
        {
            // Arrange
            var structures = CreateMockStructureList(
                (1, "Akua", EntityType.BA, 1)
            );

            _mockGateway
                .Setup(g => g.SendRequestAsync<GlobalStructureList>(
                    CmdId.Request_GlobalStructure_List,
                    null,
                    It.IsAny<int>()))
                .ReturnsAsync(structures);

            await _tracker.RefreshStructuresAsync();

            // Act
            var structure = _tracker.GetStructure(1);

            // Assert
            Assert.NotNull(structure);
            Assert.Equal(1, structure.Value.id);
        }

        [Fact]
        public void GetStructure_WithUnknownId_ShouldReturnNull()
        {
            // Act
            var structure = _tracker.GetStructure(99999);

            // Assert
            Assert.Null(structure);
        }

        [Fact]
        public async Task StructureExists_ShouldReturnCorrectValue()
        {
            // Arrange
            var structures = CreateMockStructureList(
                (1, "Akua", EntityType.BA, 1)
            );

            _mockGateway
                .Setup(g => g.SendRequestAsync<GlobalStructureList>(
                    CmdId.Request_GlobalStructure_List,
                    null,
                    It.IsAny<int>()))
                .ReturnsAsync(structures);

            await _tracker.RefreshStructuresAsync();

            // Act & Assert
            Assert.True(_tracker.StructureExists(1));
            Assert.False(_tracker.StructureExists(99999));
        }

        [Fact]
        public void ModuleName_ShouldReturnStructureTracker()
        {
            // Assert
            Assert.Equal("StructureTracker", _tracker.ModuleName);
        }

        [Fact]
        public void UpdatePriority_ShouldReturn20()
        {
            // Assert
            Assert.Equal(20, _tracker.UpdatePriority);
        }

        [Fact]
        public async Task GetStructuresOnPlayfield_WithEmptyPlayfield_ShouldReturnEmptyList()
        {
            // Act
            var structures = _tracker.GetStructuresOnPlayfield("");

            // Assert
            Assert.Empty(structures);
        }

        [Fact]
        public async Task GetStructuresOnPlayfield_WithUnknownPlayfield_ShouldReturnEmptyList()
        {
            // Act
            var structures = _tracker.GetStructuresOnPlayfield("UnknownPlayfield");

            // Assert
            Assert.Empty(structures);
        }

        private GlobalStructureList CreateMockStructureList(params (int id, string playfield, EntityType type, int factionId)[] structures)
        {
            var list = new GlobalStructureList
            {
                globalStructures = new Dictionary<string, List<GlobalStructureInfo>>()
            };

            foreach (var (id, playfieldName, type, factionId) in structures)
            {
                // Добавляем playfield в словарь если его еще нет
                if (!list.globalStructures.ContainsKey(playfieldName))
                {
                    list.globalStructures[playfieldName] = new List<GlobalStructureInfo>();
                }

                list.globalStructures[playfieldName].Add(new GlobalStructureInfo
                {
                    id = id,
                    type = (byte)type,
                    factionId = factionId,
                    name = $"Structure_{id}",
                    pos = new PVector3(1000, 100, 2000),
                    rot = new PVector3(0, 0, 0)
                });
            }

            return list;
        }
    }
}
