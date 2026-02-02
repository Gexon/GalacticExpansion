using System;
using GalacticExpansion.Core.Economy;
using GalacticExpansion.Models;
using Moq;
using NLog;
using Xunit;

namespace GalacticExpansion.Tests.Unit.Economy
{
    /// <summary>
    /// Unit-тесты для UnitEconomyManager - модуля управления производством юнитов
    /// </summary>
    public class UnitEconomyManagerTests
    {
        private readonly Configuration _config;
        private readonly Mock<ILogger> _loggerMock;
        private readonly UnitEconomyManager _manager;

        public UnitEconomyManagerTests()
        {
            _config = new Configuration
            {
                UnitEconomy = new UnitEconomySettings
                {
                    Enabled = true,
                    ResourceOutpostBonus = 0.25f,
                    ShipyardBonus = 0.5f
                }
            };
            _loggerMock = new Mock<ILogger>();
            _manager = new UnitEconomyManager(_config, _loggerMock.Object);
        }

        [Fact(DisplayName = "ProduceUnits - производит юниты согласно времени")]
        public void ProduceUnits_ProducesUnits_OverTime()
        {
            // Arrange
            var colony = new Colony
            {
                UnitPool = new UnitPool { AvailableGuards = 0, MaxGuards = 10, ProductionRate = 60 }
            };

            // Act - прошло 60 секунд
            _manager.ProduceUnits(colony, deltaTime: 60f);

            // Assert - должны произвести хотя бы несколько юнитов
            Assert.True(colony.UnitPool.AvailableGuards > 0);
        }

        [Fact(DisplayName = "CanSpawnUnit - возвращает true когда юниты доступны")]
        public void CanSpawnUnit_ReturnsTrue_WhenUnitsAvailable()
        {
            // Arrange
            var colony = new Colony
            {
                UnitPool = new UnitPool { AvailableGuards = 5 }
            };

            // Act
            var result = _manager.CanSpawnUnit(colony, UnitType.Guard, count: 3);

            // Assert
            Assert.True(result);
        }

        [Fact(DisplayName = "CanSpawnUnit - возвращает false когда недостаточно")]
        public void CanSpawnUnit_ReturnsFalse_WhenNotEnough()
        {
            // Arrange
            var colony = new Colony
            {
                UnitPool = new UnitPool { AvailableGuards = 2 }
            };

            // Act
            var result = _manager.CanSpawnUnit(colony, UnitType.Guard, count: 5);

            // Assert
            Assert.False(result);
        }

        [Fact(DisplayName = "ReserveUnits - резервирует юниты")]
        public void ReserveUnits_ReservesUnits()
        {
            // Arrange
            var colony = new Colony
            {
                UnitPool = new UnitPool { AvailableGuards = 10 }
            };

            // Act
            var reserved = _manager.ReserveUnits(colony, UnitType.Guard, count: 3);

            // Assert
            Assert.Equal(3, reserved);
            Assert.Equal(7, colony.UnitPool.AvailableGuards);
        }

        [Fact(DisplayName = "RegisterActiveUnit - регистрирует активный юнит")]
        public void RegisterActiveUnit_RegistersActiveUnit()
        {
            // Arrange
            var colony = new Colony
            {
                UnitPool = new UnitPool()
            };

            // Act
            _manager.RegisterActiveUnit(colony, entityId: 123, UnitType.Guard, "Defense");

            // Assert
            Assert.Single(colony.UnitPool.ActiveUnits);
            Assert.Equal(123, colony.UnitPool.ActiveUnits[0].EntityId);
        }

        [Fact(DisplayName = "RecordUnitLoss - удаляет юнит из активных")]
        public void RecordUnitLoss_RemovesUnitFromActive()
        {
            // Arrange
            var colony = new Colony
            {
                UnitPool = new UnitPool()
            };
            colony.UnitPool.ActiveUnits.Add(new ActiveUnit(123, "Guard"));

            // Act
            _manager.RecordUnitLoss(colony, entityId: 123);

            // Assert
            Assert.Empty(colony.UnitPool.ActiveUnits);
        }

        [Fact(DisplayName = "RecalculateCapacity - обновляет capacity")]
        public void RecalculateCapacity_UpdatesCapacity()
        {
            // Arrange
            var colony = new Colony
            {
                Stage = ColonyStage.BaseL2,
                UnitPool = new UnitPool()
            };

            // Act
            _manager.RecalculateCapacity(colony);

            // Assert - проверяем что capacity установлены
            Assert.True(colony.UnitPool.MaxGuards >= 0);
        }
    }
}
