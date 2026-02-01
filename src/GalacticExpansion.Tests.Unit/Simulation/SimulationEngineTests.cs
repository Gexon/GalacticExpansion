using System;
using System.Threading;
using System.Threading.Tasks;
using GalacticExpansion.Core.Simulation;
using GalacticExpansion.Core.Simulation.Events;
using GalacticExpansion.Core.State;
using GalacticExpansion.Models;
using Moq;
using NLog;
using Xunit;

namespace GalacticExpansion.Tests.Unit.Simulation
{
    /// <summary>
    /// Unit-тесты для SimulationEngine.
    /// Проверяют корректность работы главного цикла симуляции.
    /// </summary>
    public class SimulationEngineTests
    {
        private readonly Mock<IStateStore> _mockStateStore;
        private readonly Mock<IModuleRegistry> _mockModuleRegistry;
        private readonly Mock<IEventBus> _mockEventBus;
        private readonly Mock<ILogger> _mockLogger;
        private readonly SimulationEngine _engine;

        public SimulationEngineTests()
        {
            _mockStateStore = new Mock<IStateStore>();
            _mockModuleRegistry = new Mock<IModuleRegistry>();
            _mockEventBus = new Mock<IEventBus>();
            _mockLogger = new Mock<ILogger>();

            // Setup default behavior
            _mockStateStore
                .Setup(s => s.LoadAsync())
                .ReturnsAsync(new SimulationState());
            _mockStateStore
                .Setup(s => s.SaveAsync(It.IsAny<SimulationState>()))
                .Returns(Task.CompletedTask);
            _mockModuleRegistry
                .Setup(r => r.InitializeAllModulesAsync(It.IsAny<SimulationState>()))
                .Returns(Task.CompletedTask);
            _mockModuleRegistry
                .Setup(r => r.ShutdownAllModulesAsync())
                .Returns(Task.CompletedTask);

            _engine = new SimulationEngine(
                _mockStateStore.Object,
                _mockModuleRegistry.Object,
                _mockEventBus.Object,
                _mockLogger.Object
            );
        }

        [Fact]
        public async Task StartAsync_ShouldLoadState()
        {
            // Act
            await _engine.StartAsync();

            // Assert
            _mockStateStore.Verify(s => s.LoadAsync(), Times.Once);
            
            // Cleanup
            await _engine.StopAsync();
        }

        [Fact]
        public async Task StartAsync_ShouldInitializeModules()
        {
            // Act
            await _engine.StartAsync();

            // Assert
            _mockModuleRegistry.Verify(
                r => r.InitializeAllModulesAsync(It.IsAny<SimulationState>()),
                Times.Once
            );
            
            // Cleanup
            await _engine.StopAsync();
        }

        [Fact]
        public async Task StartAsync_ShouldPublishStartedEvent()
        {
            // Act
            await _engine.StartAsync();

            // Assert
            _mockEventBus.Verify(
                e => e.Publish(It.Is<SimulationStartedEvent>(evt => evt.ModuleCount == 0)),
                Times.Once
            );
            
            // Cleanup
            await _engine.StopAsync();
        }

        [Fact]
        public async Task StartAsync_ShouldPublishStartedEventWithCorrectModuleCount()
        {
            // Arrange - используем реальный ModuleRegistry для этого теста
            var realRegistry = new ModuleRegistry(_mockLogger.Object);
            var engineWithRealRegistry = new SimulationEngine(
                _mockStateStore.Object,
                realRegistry,
                _mockEventBus.Object,
                _mockLogger.Object
            );

            var mockModule1 = new Mock<ISimulationModule>();
            mockModule1.Setup(m => m.ModuleName).Returns("Module1");
            mockModule1.Setup(m => m.UpdatePriority).Returns(10);
            mockModule1.Setup(m => m.InitializeAsync(It.IsAny<SimulationState>()))
                .Returns(Task.CompletedTask);
            mockModule1.Setup(m => m.ShutdownAsync())
                .Returns(Task.CompletedTask);

            var mockModule2 = new Mock<ISimulationModule>();
            mockModule2.Setup(m => m.ModuleName).Returns("Module2");
            mockModule2.Setup(m => m.UpdatePriority).Returns(20);
            mockModule2.Setup(m => m.InitializeAsync(It.IsAny<SimulationState>()))
                .Returns(Task.CompletedTask);
            mockModule2.Setup(m => m.ShutdownAsync())
                .Returns(Task.CompletedTask);

            engineWithRealRegistry.RegisterModule(mockModule1.Object);
            engineWithRealRegistry.RegisterModule(mockModule2.Object);

            // Act
            await engineWithRealRegistry.StartAsync();

            // Assert
            _mockEventBus.Verify(
                e => e.Publish(It.Is<SimulationStartedEvent>(evt => evt.ModuleCount == 2)),
                Times.Once
            );
            
            // Cleanup
            await engineWithRealRegistry.StopAsync();
        }

        [Fact]
        public async Task StartAsync_ShouldSetIsRunningToTrue()
        {
            // Act
            await _engine.StartAsync();

            // Assert
            Assert.True(_engine.IsRunning);
            
            // Cleanup
            await _engine.StopAsync();
        }

        [Fact]
        public async Task StartAsync_WhenAlreadyRunning_ShouldNotStartAgain()
        {
            // Arrange
            await _engine.StartAsync();

            // Act
            await _engine.StartAsync(); // Second call

            // Assert - LoadAsync should only be called once
            _mockStateStore.Verify(s => s.LoadAsync(), Times.Once);
            
            // Cleanup
            await _engine.StopAsync();
        }

        [Fact]
        public async Task StopAsync_ShouldSaveState()
        {
            // Arrange
            await _engine.StartAsync();

            // Act
            await _engine.StopAsync();

            // Assert
            _mockStateStore.Verify(
                s => s.SaveAsync(It.IsAny<SimulationState>()),
                Times.AtLeastOnce
            );
        }

        [Fact]
        public async Task StopAsync_ShouldShutdownModules()
        {
            // Arrange
            await _engine.StartAsync();

            // Act
            await _engine.StopAsync();

            // Assert
            _mockModuleRegistry.Verify(
                r => r.ShutdownAllModulesAsync(),
                Times.Once
            );
        }

        [Fact]
        public async Task StopAsync_ShouldSetIsRunningToFalse()
        {
            // Arrange
            await _engine.StartAsync();

            // Act
            await _engine.StopAsync();

            // Assert
            Assert.False(_engine.IsRunning);
        }

        [Fact]
        public async Task StopAsync_WhenNotRunning_ShouldNotThrow()
        {
            // Act & Assert
            var exception = await Record.ExceptionAsync(async () => 
                await _engine.StopAsync()
            );
            
            Assert.Null(exception);
        }

        [Fact]
        public async Task SimulationTick_ShouldUpdateModules()
        {
            // Arrange
            await _engine.StartAsync();

            // Wait for at least one tick (1 second + buffer)
            await Task.Delay(1500);

            // Act
            await _engine.StopAsync();

            // Assert - UpdateAllModules should be called at least once
            _mockModuleRegistry.Verify(
                r => r.UpdateAllModules(It.IsAny<SimulationContext>()),
                Times.AtLeastOnce
            );
        }

        [Fact]
        public async Task SimulationTick_ShouldPublishTickEvent()
        {
            // Arrange
            await _engine.StartAsync();

            // Wait for at least one tick
            await Task.Delay(1500);

            // Act
            await _engine.StopAsync();

            // Assert
            _mockEventBus.Verify(
                e => e.Publish(It.IsAny<SimulationTickEvent>()),
                Times.AtLeastOnce
            );
        }

        [Fact]
        public async Task RegisterModule_BeforeStart_ShouldSucceed()
        {
            // Arrange
            var mockModule = new Mock<ISimulationModule>();
            mockModule.Setup(m => m.ModuleName).Returns("TestModule");
            mockModule.Setup(m => m.UpdatePriority).Returns(10);

            // Act
            _engine.RegisterModule(mockModule.Object);

            // Assert
            _mockModuleRegistry.Verify(
                r => r.RegisterModule(mockModule.Object),
                Times.Once
            );
        }

        [Fact]
        public async Task RegisterModule_AfterStart_ShouldThrow()
        {
            // Arrange
            await _engine.StartAsync();
            var mockModule = new Mock<ISimulationModule>();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => 
                _engine.RegisterModule(mockModule.Object)
            );
            
            // Cleanup
            await _engine.StopAsync();
        }

        [Fact]
        public async Task State_ShouldReturnCurrentState()
        {
            // Arrange
            var testState = new SimulationState { Version = 42 };
            _mockStateStore
                .Setup(s => s.LoadAsync())
                .ReturnsAsync(testState);

            // Act
            await _engine.StartAsync();
            var state = _engine.State;

            // Assert
            Assert.NotNull(state);
            Assert.Equal(42, state.Version);
            
            // Cleanup
            await _engine.StopAsync();
        }

        [Fact]
        public async Task StartAsync_WithFailedStateLoad_ShouldThrow()
        {
            // Arrange
            _mockStateStore
                .Setup(s => s.LoadAsync())
                .ThrowsAsync(new Exception("Failed to load state"));

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(async () => 
                await _engine.StartAsync()
            );
            
            Assert.False(_engine.IsRunning);
        }

        [Fact]
        public async Task StartAsync_WithFailedModuleInit_ShouldThrow()
        {
            // Arrange
            _mockModuleRegistry
                .Setup(r => r.InitializeAllModulesAsync(It.IsAny<SimulationState>()))
                .ThrowsAsync(new Exception("Failed to initialize modules"));

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(async () => 
                await _engine.StartAsync()
            );
            
            Assert.False(_engine.IsRunning);
        }
    }
}
