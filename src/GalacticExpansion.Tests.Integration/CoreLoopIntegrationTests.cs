using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GalacticExpansion.Core.Simulation;
using GalacticExpansion.Core.Simulation.Events;
using GalacticExpansion.Core.State;
using GalacticExpansion.Core.Tracking;
using GalacticExpansion.Models;
using Moq;
using NLog;
using Xunit;

namespace GalacticExpansion.Tests.Integration
{
    /// <summary>
    /// Integration-тесты для полного цикла Phase 2: Core Loop.
    /// Проверяют взаимодействие всех компонентов вместе.
    /// </summary>
    public class CoreLoopIntegrationTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly Mock<ILogger> _mockLogger;

        public CoreLoopIntegrationTests()
        {
            // Создаем временную директорию для тестов
            _testDirectory = Path.Combine(Path.GetTempPath(), $"GLEX_IntegrationTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);

            _mockLogger = new Mock<ILogger>();
        }

        public void Dispose()
        {
            // Очищаем временную директорию после тестов
            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, recursive: true);
                }
                catch
                {
                    // Игнорируем ошибки очистки
                }
            }
        }

        [Fact]
        public async Task FullCycle_StartTicksStop_ShouldWorkCorrectly()
        {
            // Arrange
            var stateStore = new StateStore(_testDirectory);
            var eventBus = new EventBus(_mockLogger.Object);
            var moduleRegistry = new ModuleRegistry(_mockLogger.Object);
            
            var simulationEngine = new SimulationEngine(
                stateStore,
                moduleRegistry,
                eventBus,
                _mockLogger.Object
            );

            // Счетчики для проверки событий
            int startedEventCount = 0;
            int tickEventCount = 0;

            eventBus.Subscribe<SimulationStartedEvent>(e => startedEventCount++);
            eventBus.Subscribe<SimulationTickEvent>(e => tickEventCount++);

            // Act
            await simulationEngine.StartAsync();
            Assert.True(simulationEngine.IsRunning, "Engine should be running after start");

            // Ждем несколько тиков (минимум 3 секунды для 3 тиков)
            await Task.Delay(3500);

            await simulationEngine.StopAsync();
            Assert.False(simulationEngine.IsRunning, "Engine should not be running after stop");

            // Assert
            Assert.Equal(1, startedEventCount); // Одно событие старта
            Assert.True(tickEventCount >= 3, $"Should have at least 3 tick events, got {tickEventCount}"); // Минимум 3 тика
            
            // Проверяем, что state был сохранен
            Assert.True(File.Exists(Path.Combine(_testDirectory, "state.json")));
        }

        [Fact]
        public async Task SimulationEngine_WithModules_ShouldInitializeAndUpdateThem()
        {
            // Arrange
            var stateStore = new StateStore(_testDirectory);
            var eventBus = new EventBus(_mockLogger.Object);
            var moduleRegistry = new ModuleRegistry(_mockLogger.Object);
            
            var simulationEngine = new SimulationEngine(
                stateStore,
                moduleRegistry,
                eventBus,
                _mockLogger.Object
            );

            // Создаем mock модуль
            var mockModule = new Mock<ISimulationModule>();
            mockModule.Setup(m => m.ModuleName).Returns("TestModule");
            mockModule.Setup(m => m.UpdatePriority).Returns(10);
            mockModule.Setup(m => m.InitializeAsync(It.IsAny<SimulationState>()))
                .Returns(Task.CompletedTask);
            mockModule.Setup(m => m.ShutdownAsync())
                .Returns(Task.CompletedTask);

            int updateCount = 0;
            mockModule.Setup(m => m.OnSimulationUpdate(It.IsAny<SimulationContext>()))
                .Callback(() => updateCount++);

            simulationEngine.RegisterModule(mockModule.Object);

            // Act
            await simulationEngine.StartAsync();
            await Task.Delay(2500); // Ждем ~2 тика
            await simulationEngine.StopAsync();

            // Assert
            mockModule.Verify(m => m.InitializeAsync(It.IsAny<SimulationState>()), Times.Once);
            mockModule.Verify(m => m.ShutdownAsync(), Times.Once);
            Assert.True(updateCount >= 2, $"Module should be updated at least 2 times, got {updateCount}");
        }

        [Fact]
        public async Task SimulationEngine_ShouldPersistState()
        {
            // Arrange
            var stateStore = new StateStore(_testDirectory);
            var eventBus = new EventBus(_mockLogger.Object);
            var moduleRegistry = new ModuleRegistry(_mockLogger.Object);
            
            var simulationEngine = new SimulationEngine(
                stateStore,
                moduleRegistry,
                eventBus,
                _mockLogger.Object
            );

            // Act - Первый запуск
            await simulationEngine.StartAsync();
            
            // Модифицируем state
            var state = simulationEngine.State;
            state.Colonies.Add(new Colony 
            { 
                Id = "test-colony-1",
                Playfield = "Akua",
                Stage = ColonyStage.BaseL1
            });
            state.IsDirty = true;

            await Task.Delay(1500);
            await simulationEngine.StopAsync();

            // Act - Второй запуск (должен загрузить сохраненное состояние)
            var simulationEngine2 = new SimulationEngine(
                stateStore,
                new ModuleRegistry(_mockLogger.Object),
                new EventBus(_mockLogger.Object),
                _mockLogger.Object
            );

            await simulationEngine2.StartAsync();
            var loadedState = simulationEngine2.State;
            await simulationEngine2.StopAsync();

            // Assert
            Assert.Single(loadedState.Colonies);
            Assert.Equal("test-colony-1", loadedState.Colonies[0].Id);
            Assert.Equal("Akua", loadedState.Colonies[0].Playfield);
        }

        [Fact]
        public async Task EventBus_ShouldPropagateEventsBetweenModules()
        {
            // Arrange
            var eventBus = new EventBus(_mockLogger.Object);
            
            bool module1ReceivedEvent = false;
            bool module2ReceivedEvent = false;

            eventBus.Subscribe<SimulationStartedEvent>(e => module1ReceivedEvent = true);
            eventBus.Subscribe<SimulationStartedEvent>(e => module2ReceivedEvent = true);

            // Act
            eventBus.Publish(new SimulationStartedEvent());

            // Assert
            Assert.True(module1ReceivedEvent, "Module 1 should receive event");
            Assert.True(module2ReceivedEvent, "Module 2 should receive event");
        }

        [Fact]
        public async Task ModuleRegistry_ShouldHandleModuleFailureGracefully()
        {
            // Arrange
            var moduleRegistry = new ModuleRegistry(_mockLogger.Object);
            
            var goodModule = new Mock<ISimulationModule>();
            goodModule.Setup(m => m.ModuleName).Returns("GoodModule");
            goodModule.Setup(m => m.UpdatePriority).Returns(10);
            goodModule.Setup(m => m.InitializeAsync(It.IsAny<SimulationState>()))
                .Returns(Task.CompletedTask);

            var failingModule = new Mock<ISimulationModule>();
            failingModule.Setup(m => m.ModuleName).Returns("FailingModule");
            failingModule.Setup(m => m.UpdatePriority).Returns(20);
            failingModule.Setup(m => m.InitializeAsync(It.IsAny<SimulationState>()))
                .ThrowsAsync(new Exception("Test exception"));

            moduleRegistry.RegisterModule(goodModule.Object);
            moduleRegistry.RegisterModule(failingModule.Object);

            var state = new SimulationState();

            // Act
            await moduleRegistry.InitializeAllModulesAsync(state);

            // Assert - Good module should still be initialized despite failing module
            goodModule.Verify(m => m.InitializeAsync(state), Times.Once);
            failingModule.Verify(m => m.InitializeAsync(state), Times.Once);
        }

        [Fact]
        public async Task SimulationEngine_ShouldHandleMultipleStartStopCycles()
        {
            // Arrange
            var stateStore = new StateStore(_testDirectory);
            var eventBus = new EventBus(_mockLogger.Object);
            var moduleRegistry = new ModuleRegistry(_mockLogger.Object);
            
            var simulationEngine = new SimulationEngine(
                stateStore,
                moduleRegistry,
                eventBus,
                _mockLogger.Object
            );

            // Act & Assert - Цикл 1
            await simulationEngine.StartAsync();
            Assert.True(simulationEngine.IsRunning);
            await Task.Delay(1000);
            await simulationEngine.StopAsync();
            Assert.False(simulationEngine.IsRunning);

            // Act & Assert - Цикл 2
            await simulationEngine.StartAsync();
            Assert.True(simulationEngine.IsRunning);
            await Task.Delay(1000);
            await simulationEngine.StopAsync();
            Assert.False(simulationEngine.IsRunning);
        }
    }
}
