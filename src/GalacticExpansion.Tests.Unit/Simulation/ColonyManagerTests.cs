using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalacticExpansion.Core.Economy;
using GalacticExpansion.Core.Simulation;
using GalacticExpansion.Core.Spawning;
using GalacticExpansion.Core.State;
using GalacticExpansion.Models;
using Moq;
using NLog;
using Xunit;

namespace GalacticExpansion.Tests.Unit.Simulation
{
    /// <summary>
    /// Unit-тесты для ColonyManager - координатора управления колониями.
    /// Проверяются:
    /// - Координация обновлений (экономика, юниты, апгрейды)
    /// - Создание новых колоний
    /// - Удаление колоний из системы
    /// - Обработка ошибок при обновлении
    /// </summary>
    public class ColonyManagerTests
    {
        private readonly Mock<IStageManager> _stageManagerMock;
        private readonly Mock<IEconomySimulator> _economySimulatorMock;
        private readonly Mock<IUnitEconomyManager> _unitEconomyMock;
        private readonly Mock<IStateStore> _stateStoreMock;
        private readonly Mock<ILogger> _loggerMock;
        private readonly ColonyManager _colonyManager;

        public ColonyManagerTests()
        {
            _stageManagerMock = new Mock<IStageManager>();
            _economySimulatorMock = new Mock<IEconomySimulator>();
            _unitEconomyMock = new Mock<IUnitEconomyManager>();
            _stateStoreMock = new Mock<IStateStore>();
            _loggerMock = new Mock<ILogger>();

            // По умолчанию: апгрейд не готов
            _stageManagerMock.Setup(s => s.CanTransitionToNextStageAsync(It.IsAny<Colony>())).ReturnsAsync(false);
            _stageManagerMock.Setup(s => s.TransitionToNextStageAsync(It.IsAny<Colony>())).Returns(Task.CompletedTask);
            _stageManagerMock.Setup(s => s.MaintainColonyStructuresAsync(It.IsAny<Colony>())).Returns(Task.CompletedTask);

            _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(new SimulationState());
            _stateStoreMock.Setup(s => s.SaveAsync(It.IsAny<SimulationState>())).Returns(Task.CompletedTask);

            _colonyManager = new ColonyManager(
                _stageManagerMock.Object,
                _economySimulatorMock.Object,
                _unitEconomyMock.Object,
                _stateStoreMock.Object,
                _loggerMock.Object
            );
        }

        [Fact(DisplayName = "Конструктор - выбрасывает ArgumentNullException для null параметров")]
        public void Constructor_ThrowsArgumentNullException_WhenParametersAreNull()
        {
            // Assert
            Assert.Throws<ArgumentNullException>(() => new ColonyManager(
                null!, _economySimulatorMock.Object, _unitEconomyMock.Object, _stateStoreMock.Object, _loggerMock.Object));
            
            Assert.Throws<ArgumentNullException>(() => new ColonyManager(
                _stageManagerMock.Object, null!, _unitEconomyMock.Object, _stateStoreMock.Object, _loggerMock.Object));
            
            Assert.Throws<ArgumentNullException>(() => new ColonyManager(
                _stageManagerMock.Object, _economySimulatorMock.Object, null!, _stateStoreMock.Object, _loggerMock.Object));
            
            Assert.Throws<ArgumentNullException>(() => new ColonyManager(
                _stageManagerMock.Object, _economySimulatorMock.Object, _unitEconomyMock.Object, null!, _loggerMock.Object));
        }

        [Fact(DisplayName = "UpdateColony - вызывает UpdateProduction для экономики")]
        public async Task UpdateColony_CallsUpdateProduction_OnEconomySimulator()
        {
            // Arrange
            var colony = new Colony { Id = "colony-1" };
            var deltaTime = 1.0f;

            // Act
            await _colonyManager.UpdateColonyAsync(colony, deltaTime);

            // Assert
            _economySimulatorMock.Verify(e => e.UpdateProduction(colony, deltaTime), Times.Once);
        }

        [Fact(DisplayName = "UpdateColony - вызывает ProduceUnits для UnitEconomy")]
        public async Task UpdateColony_CallsProduceUnits_OnUnitEconomyManager()
        {
            // Arrange
            var colony = new Colony { Id = "colony-1" };
            var deltaTime = 1.0f;

            // Act
            await _colonyManager.UpdateColonyAsync(colony, deltaTime);

            // Assert
            _unitEconomyMock.Verify(u => u.ProduceUnits(colony, deltaTime), Times.Once);
        }

        [Fact(DisplayName = "UpdateColony - проверяет возможность апгрейда")]
        public async Task UpdateColony_ChecksCanTransition_Always()
        {
            // Arrange
            var colony = new Colony { Id = "colony-1" };

            // Act
            await _colonyManager.UpdateColonyAsync(colony, 1.0f);

            // Assert
            _stageManagerMock.Verify(s => s.CanTransitionToNextStageAsync(colony), Times.Once);
        }

        [Fact(DisplayName = "UpdateColony - выполняет апгрейд когда готово")]
        public async Task UpdateColony_PerformsTransition_WhenReady()
        {
            // Arrange
            var colony = new Colony { Id = "colony-1" };
            _stageManagerMock.Setup(s => s.CanTransitionToNextStageAsync(colony)).ReturnsAsync(true);

            // Act
            await _colonyManager.UpdateColonyAsync(colony, 1.0f);

            // Assert
            _stageManagerMock.Verify(s => s.TransitionToNextStageAsync(colony), Times.Once);
        }

        [Fact(DisplayName = "UpdateColony - НЕ выполняет апгрейд когда не готово")]
        public async Task UpdateColony_DoesNotPerformTransition_WhenNotReady()
        {
            // Arrange
            var colony = new Colony { Id = "colony-1" };
            _stageManagerMock.Setup(s => s.CanTransitionToNextStageAsync(colony)).ReturnsAsync(false);

            // Act
            await _colonyManager.UpdateColonyAsync(colony, 1.0f);

            // Assert
            _stageManagerMock.Verify(s => s.TransitionToNextStageAsync(It.IsAny<Colony>()), Times.Never);
        }

        [Fact(DisplayName = "UpdateColony - обрабатывает исключения без краша")]
        public async Task UpdateColony_HandlesExceptions_Gracefully()
        {
            // Arrange
            var colony = new Colony { Id = "colony-1" };
            _economySimulatorMock.Setup(e => e.UpdateProduction(It.IsAny<Colony>(), It.IsAny<float>()))
                .Throws(new Exception("Test exception"));

            // Act (не должно выбросить исключение)
            await _colonyManager.UpdateColonyAsync(colony, 1.0f);

            // Assert - логирование ошибки
            _loggerMock.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Once);
        }

        [Fact(DisplayName = "CreateColony - создаёт новую колонию через StageManager")]
        public async Task CreateColony_InitializesNewColony_ThroughStageManager()
        {
            // Arrange
            var playfield = "Akua";
            var position = new Vector3(1000, 100, -500);
            var factionId = 2;
            var expectedColony = new Colony 
            { 
                Id = "new-colony",
                Playfield = playfield,
                Position = position,
                Stage = ColonyStage.LandingPending
            };

            _stageManagerMock.Setup(s => s.InitializeColonyAsync(playfield, position, factionId))
                .ReturnsAsync(expectedColony);

            // Act
            var colony = await _colonyManager.CreateColonyAsync(playfield, position, factionId);

            // Assert
            Assert.NotNull(colony);
            Assert.Equal("new-colony", colony.Id);
            Assert.Equal(playfield, colony.Playfield);
            _stageManagerMock.Verify(s => s.InitializeColonyAsync(playfield, position, factionId), Times.Once);
        }

        [Fact(DisplayName = "CreateColony - добавляет колонию в state и сохраняет")]
        public async Task CreateColony_AddsColonyToState_AndSaves()
        {
            // Arrange
            var playfield = "Akua";
            var position = new Vector3(1000, 100, -500);
            var factionId = 2;
            var newColony = new Colony { Id = "new-colony" };

            var state = new SimulationState { Colonies = new List<Colony>() };
            _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(state);
            _stageManagerMock.Setup(s => s.InitializeColonyAsync(It.IsAny<string>(), It.IsAny<Vector3>(), It.IsAny<int>()))
                .ReturnsAsync(newColony);

            // Act
            await _colonyManager.CreateColonyAsync(playfield, position, factionId);

            // Assert
            Assert.Single(state.Colonies);
            Assert.Equal("new-colony", state.Colonies[0].Id);
            _stateStoreMock.Verify(s => s.SaveAsync(state), Times.Once);
        }

        [Fact(DisplayName = "RemoveColony - удаляет колонию из state")]
        public async Task RemoveColony_RemovesColonyFromState_Correctly()
        {
            // Arrange
            var colonyToRemove = new Colony { Id = "colony-to-remove" };
            var state = new SimulationState 
            { 
                Colonies = new List<Colony> { colonyToRemove, new Colony { Id = "colony-2" } }
            };
            _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(state);

            // Act
            await _colonyManager.RemoveColonyAsync("colony-to-remove");

            // Assert
            Assert.Single(state.Colonies);
            Assert.Equal("colony-2", state.Colonies[0].Id);
            _stateStoreMock.Verify(s => s.SaveAsync(state), Times.Once);
        }

        [Fact(DisplayName = "RemoveColony - логирует предупреждение если колония не найдена")]
        public async Task RemoveColony_LogsWarning_WhenColonyNotFound()
        {
            // Arrange
            var state = new SimulationState { Colonies = new List<Colony>() };
            _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(state);

            // Act
            await _colonyManager.RemoveColonyAsync("non-existent-colony");

            // Assert
            _loggerMock.Verify(l => l.Warn(It.Is<string>(s => s.Contains("not found"))), Times.Once);
        }

        [Fact(DisplayName = "RemoveColony - НЕ сохраняет state если колония не найдена")]
        public async Task RemoveColony_DoesNotSaveState_WhenColonyNotFound()
        {
            // Arrange
            var state = new SimulationState { Colonies = new List<Colony>() };
            _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(state);

            // Act
            await _colonyManager.RemoveColonyAsync("non-existent-colony");

            // Assert
            _stateStoreMock.Verify(s => s.SaveAsync(It.IsAny<SimulationState>()), Times.Never);
        }
    }
}
