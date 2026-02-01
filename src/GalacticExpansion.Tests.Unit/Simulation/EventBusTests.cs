using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GalacticExpansion.Core.Simulation;
using GalacticExpansion.Core.Simulation.Events;
using Moq;
using NLog;
using Xunit;

namespace GalacticExpansion.Tests.Unit.Simulation
{
    /// <summary>
    /// Unit-тесты для EventBus.
    /// Проверяют корректность работы publish-subscribe механизма.
    /// </summary>
    public class EventBusTests
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly EventBus _eventBus;

        public EventBusTests()
        {
            _mockLogger = new Mock<ILogger>();
            _eventBus = new EventBus(_mockLogger.Object);
        }

        [Fact]
        public void Subscribe_ShouldAddHandler()
        {
            // Arrange
            var handlerCalled = false;
            Action<SimulationStartedEvent> handler = (e) => handlerCalled = true;

            // Act
            _eventBus.Subscribe(handler);
            _eventBus.Publish(new SimulationStartedEvent());

            // Assert
            Assert.True(handlerCalled, "Handler should be called after subscription");
        }

        [Fact]
        public void Subscribe_ShouldNotAddDuplicateHandler()
        {
            // Arrange
            var callCount = 0;
            Action<SimulationStartedEvent> handler = (e) => callCount++;

            // Act
            _eventBus.Subscribe(handler);
            _eventBus.Subscribe(handler); // Duplicate
            _eventBus.Publish(new SimulationStartedEvent());

            // Assert
            Assert.Equal(1, callCount);
        }

        [Fact]
        public void Unsubscribe_ShouldRemoveHandler()
        {
            // Arrange
            var handlerCalled = false;
            Action<SimulationStartedEvent> handler = (e) => handlerCalled = true;

            // Act
            _eventBus.Subscribe(handler);
            _eventBus.Unsubscribe(handler);
            _eventBus.Publish(new SimulationStartedEvent());

            // Assert
            Assert.False(handlerCalled, "Handler should not be called after unsubscription");
        }

        [Fact]
        public void Publish_WithMultipleSubscribers_ShouldCallAll()
        {
            // Arrange
            var callCount = 0;
            Action<SimulationStartedEvent> handler1 = (e) => callCount++;
            Action<SimulationStartedEvent> handler2 = (e) => callCount++;
            Action<SimulationStartedEvent> handler3 = (e) => callCount++;

            // Act
            _eventBus.Subscribe(handler1);
            _eventBus.Subscribe(handler2);
            _eventBus.Subscribe(handler3);
            _eventBus.Publish(new SimulationStartedEvent());

            // Assert
            Assert.Equal(3, callCount);
        }

        [Fact]
        public void Publish_WithNoSubscribers_ShouldNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => 
                _eventBus.Publish(new SimulationStartedEvent())
            );
            
            Assert.Null(exception);
        }

        [Fact]
        public void Publish_WithFailingHandler_ShouldNotBreakOtherHandlers()
        {
            // Arrange
            var handler1Called = false;
            var handler2Called = false;

            Action<SimulationStartedEvent> handler1 = (e) => handler1Called = true;
            Action<SimulationStartedEvent> failingHandler = (e) => throw new Exception("Test exception");
            Action<SimulationStartedEvent> handler2 = (e) => handler2Called = true;

            // Act
            _eventBus.Subscribe(handler1);
            _eventBus.Subscribe(failingHandler);
            _eventBus.Subscribe(handler2);
            _eventBus.Publish(new SimulationStartedEvent());

            // Assert
            Assert.True(handler1Called, "First handler should be called");
            Assert.True(handler2Called, "Third handler should be called despite second handler failure");
        }

        [Fact]
        public void Publish_WithDifferentEventTypes_ShouldIsolate()
        {
            // Arrange
            var startedCalled = false;
            var tickCalled = false;

            Action<SimulationStartedEvent> startedHandler = (e) => startedCalled = true;
            Action<SimulationTickEvent> tickHandler = (e) => tickCalled = true;

            // Act
            _eventBus.Subscribe(startedHandler);
            _eventBus.Subscribe(tickHandler);
            _eventBus.Publish(new SimulationStartedEvent());

            // Assert
            Assert.True(startedCalled, "Started handler should be called");
            Assert.False(tickCalled, "Tick handler should NOT be called for different event type");
        }

        [Fact]
        public void EventBus_ShouldBeThreadSafe()
        {
            // Arrange
            var callCount = 0;
            var lockObj = new object();
            Action<SimulationStartedEvent> handler = (e) => 
            {
                lock (lockObj) { callCount++; }
            };

            // Act - Subscribe and publish from multiple threads
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() => _eventBus.Subscribe(handler)));
            }
            Task.WaitAll(tasks.ToArray());

            tasks.Clear();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() => _eventBus.Publish(new SimulationStartedEvent())));
            }
            Task.WaitAll(tasks.ToArray());

            // Assert - Should handle concurrent access without exceptions
            Assert.True(callCount > 0, "At least one handler should be called");
        }

        [Fact]
        public void Subscribe_WithNullHandler_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                _eventBus.Subscribe<SimulationStartedEvent>(null!)
            );
        }

        [Fact]
        public void Publish_WithNullEvent_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                _eventBus.Publish<SimulationStartedEvent>(null!)
            );
        }
    }
}
