using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Economy;
using GalacticExpansion.Core.Gateway;
using GalacticExpansion.Core.Placement;
using GalacticExpansion.Core.Simulation;
using GalacticExpansion.Core.Spawning;
using GalacticExpansion.Core.State;
using GalacticExpansion.Models;
using Moq;
using NLog;
using Xunit;

namespace GalacticExpansion.Tests.Unit.Spawning
{
    /// <summary>
    /// Unit-тесты для StageManager - критичного модуля управления жизненным циклом колоний.
    /// Проверяются:
    /// - Валидация переходов между стадиями
    /// - Корректность апгрейдов (удаление старой структуры, спавн новой)
    /// - Downgrade при разрушении базы
    /// - Защита от decay (Structure Touch)
    /// - Инициализация новых колоний
    /// </summary>
    public class StageManagerTests
    {
        private readonly Mock<IEmpyrionGateway> _gatewayMock;
        private readonly Mock<IEntitySpawner> _entitySpawnerMock;
        private readonly Mock<IPlacementResolver> _placementResolverMock;
        private readonly Mock<IEconomySimulator> _economySimulatorMock;
        private readonly Mock<IUnitEconomyManager> _unitEconomyMock;
        private readonly Mock<IStateStore> _stateStoreMock;
        private readonly Mock<IEventBus> _eventBusMock;
        private readonly Mock<ILogger> _loggerMock;
        private readonly Configuration _config;
        private readonly StageManager _stageManager;

        public StageManagerTests()
        {
            _gatewayMock = new Mock<IEmpyrionGateway>();
            _entitySpawnerMock = new Mock<IEntitySpawner>();
            _placementResolverMock = new Mock<IPlacementResolver>();
            _economySimulatorMock = new Mock<IEconomySimulator>();
            _unitEconomyMock = new Mock<IUnitEconomyManager>();
            _stateStoreMock = new Mock<IStateStore>();
            _eventBusMock = new Mock<IEventBus>();
            _loggerMock = new Mock<ILogger>();

            // Минимальная конфигурация для тестов
            _config = new Configuration
            {
                Zirax = new ZiraxSettings
                {
                    FactionId = 2,
                    Stages = new List<StageConfig>
                    {
                        new StageConfig { Stage = "ConstructionYard", PrefabName = "GLEX_Yard", RequiredResources = 0, MinTimeSeconds = 60 },
                        new StageConfig { Stage = "BaseL1", PrefabName = "GLEX_Base_L1", RequiredResources = 1000, MinTimeSeconds = 180 },
                        new StageConfig { Stage = "BaseL2", PrefabName = "GLEX_Base_L2", RequiredResources = 3000, MinTimeSeconds = 300 },
                        new StageConfig { Stage = "BaseMax", PrefabName = "GLEX_Base_Max", RequiredResources = 10000, MinTimeSeconds = 600 }
                    }
                }
            };

            // По умолчанию: структуры существуют, ресурсов достаточно
            _entitySpawnerMock.Setup(e => e.EntityExistsAsync(It.IsAny<int>())).ReturnsAsync(true);
            _entitySpawnerMock.Setup(e => e.SpawnStructureAtTerrainAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<float>(),
                    It.IsAny<float>(),
                    It.IsAny<int>(),
                    It.IsAny<float>()))
                .ReturnsAsync(999); // Новый EntityId для новой структуры
            _entitySpawnerMock.Setup(e => e.DestroyEntityAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
            
            _economySimulatorMock.Setup(e => e.HasEnoughResourcesForUpgrade(It.IsAny<Colony>())).Returns(true);
            _economySimulatorMock.Setup(e => e.ConsumeResourcesForUpgrade(It.IsAny<Colony>(), It.IsAny<float>()));

            _placementResolverMock.Setup(p => p.FindSuitableLocationAsync(It.IsAny<PlacementCriteria>()))
                .ReturnsAsync(new Vector3(100, 50, 200));

            // По умолчанию возвращаем пустой state, а в тестах переходов подменяем на state с колонией.
            _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(new SimulationState());
            _stateStoreMock.Setup(s => s.SaveAsync(It.IsAny<SimulationState>())).Returns(Task.CompletedTask);

            _stageManager = new StageManager(
                _gatewayMock.Object,
                _entitySpawnerMock.Object,
                _placementResolverMock.Object,
                _economySimulatorMock.Object,
                _unitEconomyMock.Object,
                _stateStoreMock.Object,
                _eventBusMock.Object,
                _config,
                _loggerMock.Object
            );
        }

        [Fact(DisplayName = "Конструктор - выбрасывает ArgumentNullException для null параметров")]
        public void Constructor_ThrowsArgumentNullException_WhenParametersAreNull()
        {
            // Assert
            Assert.Throws<ArgumentNullException>(() => new StageManager(
                null!, _entitySpawnerMock.Object, _placementResolverMock.Object, _economySimulatorMock.Object,
                _unitEconomyMock.Object, _stateStoreMock.Object, _eventBusMock.Object, _config, _loggerMock.Object));
            
            Assert.Throws<ArgumentNullException>(() => new StageManager(
                _gatewayMock.Object, null!, _placementResolverMock.Object, _economySimulatorMock.Object,
                _unitEconomyMock.Object, _stateStoreMock.Object, _eventBusMock.Object, _config, _loggerMock.Object));
            
            Assert.Throws<ArgumentNullException>(() => new StageManager(
                _gatewayMock.Object, _entitySpawnerMock.Object, null!, _economySimulatorMock.Object,
                _unitEconomyMock.Object, _stateStoreMock.Object, _eventBusMock.Object, _config, _loggerMock.Object));
        }

        [Fact(DisplayName = "CanTransitionToNextStage - возвращает false для BaseMax")]
        public async Task CanTransitionToNextStage_ReturnsFalse_WhenAlreadyAtMaxStage()
        {
            // Arrange
            var colony = new Colony { Stage = ColonyStage.BaseMax };

            // Act
            var canTransition = await _stageManager.CanTransitionToNextStageAsync(colony);

            // Assert
            Assert.False(canTransition);
        }

        [Fact(DisplayName = "CanTransitionToNextStage - возвращает false когда структура не существует")]
        public async Task CanTransitionToNextStage_ReturnsFalse_WhenMainStructureDoesNotExist()
        {
            // Arrange
            var colony = new Colony 
            { 
                Stage = ColonyStage.ConstructionYard,
                MainStructureId = 123
            };
            _entitySpawnerMock.Setup(e => e.EntityExistsAsync(123)).ReturnsAsync(false);

            // Act
            var canTransition = await _stageManager.CanTransitionToNextStageAsync(colony);

            // Assert
            Assert.False(canTransition);
        }

        [Fact(DisplayName = "CanTransitionToNextStage - возвращает false когда недостаточно ресурсов")]
        public async Task CanTransitionToNextStage_ReturnsFalse_WhenNotEnoughResources()
        {
            // Arrange
            var colony = new Colony 
            { 
                Stage = ColonyStage.ConstructionYard,
                MainStructureId = 123
            };
            _economySimulatorMock.Setup(e => e.HasEnoughResourcesForUpgrade(colony)).Returns(false);

            // Act
            var canTransition = await _stageManager.CanTransitionToNextStageAsync(colony);

            // Assert
            Assert.False(canTransition);
        }

        [Fact(DisplayName = "CanTransitionToNextStage - возвращает false когда прошло мало времени")]
        public async Task CanTransitionToNextStage_ReturnsFalse_WhenMinTimeNotElapsed()
        {
            // Arrange
            var colony = new Colony 
            { 
                Stage = ColonyStage.ConstructionYard,
                MainStructureId = 123,
                LastUpgradeTime = DateTime.UtcNow.AddSeconds(-30) // Прошло 30 секунд, требуется 60
            };

            // Act
            var canTransition = await _stageManager.CanTransitionToNextStageAsync(colony);

            // Assert
            Assert.False(canTransition);
        }

        [Fact(DisplayName = "CanTransitionToNextStage - возвращает true когда все условия выполнены")]
        public async Task CanTransitionToNextStage_ReturnsTrue_WhenAllConditionsMet()
        {
            // Arrange
            var colony = new Colony 
            { 
                Stage = ColonyStage.ConstructionYard,
                MainStructureId = 123,
                LastUpgradeTime = DateTime.UtcNow.AddSeconds(-100) // Прошло достаточно времени
            };

            // Act
            var canTransition = await _stageManager.CanTransitionToNextStageAsync(colony);

            // Assert
            Assert.True(canTransition);
        }

        [Fact(DisplayName = "TransitionToNextStage - корректно удаляет старую структуру")]
        public async Task TransitionToNextStage_DestroysOldStructure_Correctly()
        {
            // Arrange
            var colony = new Colony 
            { 
                Id = "colony-1",
                Stage = ColonyStage.ConstructionYard,
                MainStructureId = 100,
                Playfield = "Akua",
                FactionId = 2,
                Position = new Vector3(1000, 100, -500),
                LastUpgradeTime = DateTime.UtcNow.AddSeconds(-100)
            };
            _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(CreateStateWithColony(colony));

            // Act
            await _stageManager.TransitionToNextStageAsync(colony);

            // Assert
            _entitySpawnerMock.Verify(e => e.DestroyEntityAsync(100), Times.Once);
        }

        [Fact(DisplayName = "TransitionToNextStage - спавнит новую структуру следующей стадии")]
        public async Task TransitionToNextStage_SpawnsNewStructure_ForNextStage()
        {
            // Arrange
            var colony = new Colony 
            { 
                Id = "colony-1",
                Stage = ColonyStage.ConstructionYard,
                MainStructureId = 100,
                Playfield = "Akua",
                FactionId = 2,
                Position = new Vector3(1000, 100, -500),
                LastUpgradeTime = DateTime.UtcNow.AddSeconds(-100)
            };
            _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(CreateStateWithColony(colony));

            // Act
            await _stageManager.TransitionToNextStageAsync(colony);

            // Assert
            _entitySpawnerMock.Verify(e => e.SpawnStructureAtTerrainAsync(
                "Akua",
                "GLEX_Base_L1", // Следующая стадия
                It.IsAny<float>(),
                It.IsAny<float>(),
                2, // FactionId Zirax
                It.IsAny<float>()
            ), Times.Once);
        }

        [Fact(DisplayName = "TransitionToNextStage - обновляет стадию колонии")]
        public async Task TransitionToNextStage_UpdatesColonyStage_Correctly()
        {
            // Arrange
            var colony = new Colony 
            { 
                Id = "colony-1",
                Stage = ColonyStage.ConstructionYard,
                MainStructureId = 100,
                Playfield = "Akua",
                FactionId = 2,
                Position = new Vector3(1000, 100, -500),
                LastUpgradeTime = DateTime.UtcNow.AddSeconds(-100)
            };
            _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(CreateStateWithColony(colony));

            // Act
            await _stageManager.TransitionToNextStageAsync(colony);

            // Assert
            Assert.Equal(ColonyStage.BaseL1, colony.Stage);
            Assert.Equal(999, colony.MainStructureId); // Новый EntityId
            Assert.NotNull(colony.LastUpgradeTime);
        }

        [Fact(DisplayName = "TransitionToNextStage - потребляет ресурсы")]
        public async Task TransitionToNextStage_ConsumesResources_Correctly()
        {
            // Arrange
            var colony = new Colony 
            { 
                Id = "colony-1",
                Stage = ColonyStage.ConstructionYard,
                MainStructureId = 100,
                Playfield = "Akua",
                FactionId = 2,
                LastUpgradeTime = DateTime.UtcNow.AddSeconds(-100)
            };
            _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(CreateStateWithColony(colony));

            // Act
            await _stageManager.TransitionToNextStageAsync(colony);

            // Assert
            _economySimulatorMock.Verify(e => e.ConsumeResourcesForUpgrade(colony, 1000f), Times.Once);
        }

        [Fact(DisplayName = "TransitionToNextStage - публикует событие StageTransition")]
        public async Task TransitionToNextStage_PublishesEvent_OnSuccess()
        {
            // Arrange
            var colony = new Colony 
            { 
                Id = "colony-1",
                Stage = ColonyStage.ConstructionYard,
                MainStructureId = 100,
                Playfield = "Akua",
                FactionId = 2,
                LastUpgradeTime = DateTime.UtcNow.AddSeconds(-100)
            };
            _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(CreateStateWithColony(colony));

            // Act
            await _stageManager.TransitionToNextStageAsync(colony);

            // Assert
            _eventBusMock.Verify(e => e.Publish(It.Is<StageTransitionEvent>(evt =>
                evt.ColonyId == "colony-1" &&
                evt.PreviousStage == ColonyStage.ConstructionYard &&
                evt.NewStage == ColonyStage.BaseL1
            )), Times.Once);
        }

        [Fact(DisplayName = "DowngradeColony - откатывает стадию на предыдущую")]
        public async Task DowngradeColony_RollsBackToPreviousStage_Correctly()
        {
            // Arrange
            var colony = new Colony 
            { 
                Id = "colony-1",
                Stage = ColonyStage.BaseL1,
                MainStructureId = 200,
                Playfield = "Akua",
                FactionId = 2,
                Position = new Vector3(1000, 100, -500)
            };
            _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(CreateStateWithColony(colony));

            // Act
            await _stageManager.DowngradeColonyAsync(colony);

            // Assert
            Assert.Equal(ColonyStage.ConstructionYard, colony.Stage);
            // После разрушения главной структуры ID должен быть сброшен.
            Assert.Null(colony.MainStructureId);
        }

        [Fact(DisplayName = "DowngradeColony - не откатывает если уже на минимальной стадии")]
        public async Task DowngradeColony_DoesNothing_WhenAlreadyAtMinStage()
        {
            // Arrange
            var colony = new Colony 
            { 
                Id = "colony-1",
                Stage = ColonyStage.LandingPending, // Минимальная стадия
                MainStructureId = 50
            };

            // Act
            await _stageManager.DowngradeColonyAsync(colony);

            // Assert
            Assert.Equal(ColonyStage.LandingPending, colony.Stage);
            _entitySpawnerMock.Verify(e => e.SpawnStructureAsync(It.IsAny<string>(), It.IsAny<Vector3>(), It.IsAny<Vector3>(), It.IsAny<int>()), Times.Never);
        }

        [Fact(DisplayName = "MaintainColonyStructures - защищает главную структуру от decay")]
        public async Task MaintainColonyStructures_TouchesMainStructure_ToPreventDecay()
        {
            // Arrange
            var colony = new Colony 
            { 
                MainStructureId = 300
            };
            _gatewayMock.Setup(g => g.SendRequestAsync<object>(CmdId.Request_Structure_Touch, It.IsAny<Id>(), It.IsAny<int>()))
                .ReturnsAsync(new object());

            // Act
            await _stageManager.MaintainColonyStructuresAsync(colony);

            // Assert
            _gatewayMock.Verify(g => g.SendRequestAsync<object>(
                CmdId.Request_Structure_Touch,
                It.Is<Id>(id => id.id == 300),
                It.IsAny<int>()
            ), Times.Once);
        }

        [Fact(DisplayName = "InitializeColony - создаёт новую колонию с DropShip")]
        public async Task InitializeColony_CreatesNewColony_WithDropShip()
        {
            // Arrange
            var playfield = "Akua";
            var position = new Vector3(1000, 100, -500);
            var factionId = 2;

            // Act
            var colony = await _stageManager.InitializeColonyAsync(playfield, position, factionId);

            // Assert
            Assert.NotNull(colony);
            Assert.Equal(playfield, colony.Playfield);
            Assert.Equal(ColonyStage.LandingPending, colony.Stage);
            Assert.Equal(999, colony.MainStructureId);
        }

        [Fact(DisplayName = "InitializeColony - спавнит DropShip структуру")]
        public async Task InitializeColony_SpawnsDropShip_Correctly()
        {
            // Arrange
            var playfield = "Akua";
            var position = new Vector3(1000, 100, -500);
            var factionId = 2;

            // Act
            await _stageManager.InitializeColonyAsync(playfield, position, factionId);

            // Assert
            _entitySpawnerMock.Verify(e => e.SpawnStructureAtTerrainAsync(
                playfield,
                "GLEX_DropShip_T1",
                position.X,
                position.Z,
                factionId,
                10f
            ), Times.Once);
        }

        /// <summary>
        /// Создает state, содержащий переданную колонию.
        /// Используется для имитации реального поведения StateStore в тестах переходов.
        /// </summary>
        private static SimulationState CreateStateWithColony(Colony colony)
        {
            return new SimulationState
            {
                Colonies = new List<Colony> { colony }
            };
        }
    }
}
