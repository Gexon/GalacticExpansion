using System;
using System.Threading.Tasks;
using GalacticExpansion.Core.Simulation;
using GalacticExpansion.Models;
using Moq;
using NLog;
using Xunit;

namespace GalacticExpansion.Tests.Unit.Simulation
{
    /// <summary>
    /// Unit-тесты для ModuleRegistry.
    /// Проверяют корректность управления жизненным циклом модулей.
    /// </summary>
    public class ModuleRegistryTests
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly ModuleRegistry _registry;

        public ModuleRegistryTests()
        {
            _mockLogger = new Mock<ILogger>();
            _registry = new ModuleRegistry(_mockLogger.Object);
        }

        [Fact]
        public void RegisterModule_ShouldAddModule()
        {
            // Arrange
            var mockModule = CreateMockModule("TestModule", 10);

            // Act
            _registry.RegisterModule(mockModule.Object);

            // Assert
            mockModule.Verify(m => m.ModuleName, Times.AtLeastOnce);
            Assert.Equal(1, _registry.ModuleCount);
        }

        [Fact]
        public void ModuleCount_ShouldReturnCorrectCount()
        {
            // Arrange
            var module1 = CreateMockModule("Module1", 10);
            var module2 = CreateMockModule("Module2", 20);
            var module3 = CreateMockModule("Module3", 30);

            // Act & Assert
            Assert.Equal(0, _registry.ModuleCount);
            
            _registry.RegisterModule(module1.Object);
            Assert.Equal(1, _registry.ModuleCount);
            
            _registry.RegisterModule(module2.Object);
            Assert.Equal(2, _registry.ModuleCount);
            
            _registry.RegisterModule(module3.Object);
            Assert.Equal(3, _registry.ModuleCount);
        }

        [Fact]
        public void RegisterModule_WithDuplicateName_ShouldSkip()
        {
            // Arrange
            var module1 = CreateMockModule("TestModule", 10);
            var module2 = CreateMockModule("TestModule", 20);

            // Act
            _registry.RegisterModule(module1.Object);
            _registry.RegisterModule(module2.Object); // Duplicate name

            // Assert - Should log warning about duplicate
            _mockLogger.Verify(
                l => l.Warn(It.IsAny<string>()),
                Times.AtLeastOnce
            );
        }

        [Fact]
        public async Task InitializeAllModulesAsync_ShouldCallInitializeOnAllModules()
        {
            // Arrange
            var module1 = CreateMockModule("Module1", 10);
            var module2 = CreateMockModule("Module2", 20);
            var state = new SimulationState();

            _registry.RegisterModule(module1.Object);
            _registry.RegisterModule(module2.Object);

            // Act
            await _registry.InitializeAllModulesAsync(state);

            // Assert
            module1.Verify(m => m.InitializeAsync(state), Times.Once);
            module2.Verify(m => m.InitializeAsync(state), Times.Once);
        }

        [Fact]
        public async Task InitializeAllModulesAsync_WithFailingModule_ShouldContinueWithOthers()
        {
            // Arrange
            var module1 = CreateMockModule("Module1", 10);
            var failingModule = CreateMockModule("FailingModule", 20);
            var module2 = CreateMockModule("Module2", 30);
            var state = new SimulationState();

            failingModule
                .Setup(m => m.InitializeAsync(It.IsAny<SimulationState>()))
                .ThrowsAsync(new Exception("Test exception"));

            _registry.RegisterModule(module1.Object);
            _registry.RegisterModule(failingModule.Object);
            _registry.RegisterModule(module2.Object);

            // Act
            await _registry.InitializeAllModulesAsync(state);

            // Assert - All modules should be attempted
            module1.Verify(m => m.InitializeAsync(state), Times.Once);
            failingModule.Verify(m => m.InitializeAsync(state), Times.Once);
            module2.Verify(m => m.InitializeAsync(state), Times.Once);
        }

        [Fact]
        public void UpdateAllModules_ShouldCallUpdateInPriorityOrder()
        {
            // Arrange
            var callOrder = new System.Collections.Generic.List<string>();
            
            var module1 = CreateMockModule("Module1", 30);
            var module2 = CreateMockModule("Module2", 10); // Lowest priority - should be first
            var module3 = CreateMockModule("Module3", 20);

            module1.Setup(m => m.OnSimulationUpdate(It.IsAny<SimulationContext>()))
                .Callback(() => callOrder.Add("Module1"));
            module2.Setup(m => m.OnSimulationUpdate(It.IsAny<SimulationContext>()))
                .Callback(() => callOrder.Add("Module2"));
            module3.Setup(m => m.OnSimulationUpdate(It.IsAny<SimulationContext>()))
                .Callback(() => callOrder.Add("Module3"));

            _registry.RegisterModule(module1.Object);
            _registry.RegisterModule(module2.Object);
            _registry.RegisterModule(module3.Object);

            var context = new SimulationContext();

            // Act
            _registry.UpdateAllModules(context);

            // Assert - Should be called in priority order (10, 20, 30)
            Assert.Equal(3, callOrder.Count);
            Assert.Equal("Module2", callOrder[0]);
            Assert.Equal("Module3", callOrder[1]);
            Assert.Equal("Module1", callOrder[2]);
        }

        [Fact]
        public void UpdateAllModules_WithFailingModule_ShouldContinueWithOthers()
        {
            // Arrange
            var module1Called = false;
            var module2Called = false;

            var module1 = CreateMockModule("Module1", 10);
            var failingModule = CreateMockModule("FailingModule", 20);
            var module2 = CreateMockModule("Module2", 30);

            module1.Setup(m => m.OnSimulationUpdate(It.IsAny<SimulationContext>()))
                .Callback(() => module1Called = true);
            failingModule.Setup(m => m.OnSimulationUpdate(It.IsAny<SimulationContext>()))
                .Throws(new Exception("Test exception"));
            module2.Setup(m => m.OnSimulationUpdate(It.IsAny<SimulationContext>()))
                .Callback(() => module2Called = true);

            _registry.RegisterModule(module1.Object);
            _registry.RegisterModule(failingModule.Object);
            _registry.RegisterModule(module2.Object);

            var context = new SimulationContext();

            // Act
            _registry.UpdateAllModules(context);

            // Assert
            Assert.True(module1Called, "Module1 should be called");
            Assert.True(module2Called, "Module2 should be called despite FailingModule exception");
        }

        [Fact]
        public async Task ShutdownAllModulesAsync_ShouldCallShutdownOnAllModules()
        {
            // Arrange
            var module1 = CreateMockModule("Module1", 10);
            var module2 = CreateMockModule("Module2", 20);

            _registry.RegisterModule(module1.Object);
            _registry.RegisterModule(module2.Object);

            // Act
            await _registry.ShutdownAllModulesAsync();

            // Assert
            module1.Verify(m => m.ShutdownAsync(), Times.Once);
            module2.Verify(m => m.ShutdownAsync(), Times.Once);
        }

        [Fact]
        public async Task ShutdownAllModulesAsync_ShouldShutdownInReverseOrder()
        {
            // Arrange
            var shutdownOrder = new System.Collections.Generic.List<string>();
            
            var module1 = CreateMockModule("Module1", 10);
            var module2 = CreateMockModule("Module2", 20);
            var module3 = CreateMockModule("Module3", 30);

            module1.Setup(m => m.ShutdownAsync())
                .Callback(() => shutdownOrder.Add("Module1"))
                .Returns(Task.CompletedTask);
            module2.Setup(m => m.ShutdownAsync())
                .Callback(() => shutdownOrder.Add("Module2"))
                .Returns(Task.CompletedTask);
            module3.Setup(m => m.ShutdownAsync())
                .Callback(() => shutdownOrder.Add("Module3"))
                .Returns(Task.CompletedTask);

            _registry.RegisterModule(module1.Object);
            _registry.RegisterModule(module2.Object);
            _registry.RegisterModule(module3.Object);

            // Act
            await _registry.ShutdownAllModulesAsync();

            // Assert - Should shutdown in reverse priority order (30, 20, 10)
            Assert.Equal(3, shutdownOrder.Count);
            Assert.Equal("Module3", shutdownOrder[0]);
            Assert.Equal("Module2", shutdownOrder[1]);
            Assert.Equal("Module1", shutdownOrder[2]);
        }

        [Fact]
        public async Task ShutdownAllModulesAsync_WithFailingModule_ShouldContinueWithOthers()
        {
            // Arrange
            var module1Shutdown = false;
            var module2Shutdown = false;

            var module1 = CreateMockModule("Module1", 10);
            var failingModule = CreateMockModule("FailingModule", 20);
            var module2 = CreateMockModule("Module2", 30);

            module1.Setup(m => m.ShutdownAsync())
                .Callback(() => module1Shutdown = true)
                .Returns(Task.CompletedTask);
            failingModule.Setup(m => m.ShutdownAsync())
                .ThrowsAsync(new Exception("Test exception"));
            module2.Setup(m => m.ShutdownAsync())
                .Callback(() => module2Shutdown = true)
                .Returns(Task.CompletedTask);

            _registry.RegisterModule(module1.Object);
            _registry.RegisterModule(failingModule.Object);
            _registry.RegisterModule(module2.Object);

            // Act
            await _registry.ShutdownAllModulesAsync();

            // Assert
            Assert.True(module1Shutdown, "Module1 should be shut down");
            Assert.True(module2Shutdown, "Module2 should be shut down despite FailingModule exception");
        }

        private Mock<ISimulationModule> CreateMockModule(string name, int priority)
        {
            var mock = new Mock<ISimulationModule>();
            mock.Setup(m => m.ModuleName).Returns(name);
            mock.Setup(m => m.UpdatePriority).Returns(priority);
            mock.Setup(m => m.InitializeAsync(It.IsAny<SimulationState>()))
                .Returns(Task.CompletedTask);
            mock.Setup(m => m.ShutdownAsync())
                .Returns(Task.CompletedTask);
            return mock;
        }
    }
}
