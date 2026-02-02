using System;
using System.Collections.Generic;
using GalacticExpansion.Core.Economy;
using GalacticExpansion.Models;
using Moq;
using NLog;
using Xunit;

namespace GalacticExpansion.Tests.Unit.Economy
{
    /// <summary>
    /// Unit-тесты для EconomySimulator - модуля управления виртуальной экономикой колоний
    /// </summary>
    public class EconomySimulatorTests
    {
        private readonly Configuration _config;
        private readonly Mock<ILogger> _loggerMock;
        private readonly EconomySimulator _simulator;

        public EconomySimulatorTests()
        {
            _config = new Configuration
            {
                Zirax = new ZiraxSettings
                {
                    Stages = new List<StageConfig>
                    {
                        // Конфигурация выровнена с логикой апгрейдов:
                        // для перехода с ConstructionYard требуется ресурс уровня BaseL1.
                        new StageConfig { Stage = "ConstructionYard", RequiredResources = 0 },
                        new StageConfig { Stage = "BaseL1", RequiredResources = 1000 },
                        new StageConfig { Stage = "BaseL2", RequiredResources = 2000 }
                    }
                }
            };
            _loggerMock = new Mock<ILogger>();
            _simulator = new EconomySimulator(_config, _loggerMock.Object);
        }

        [Fact(DisplayName = "UpdateProduction - увеличивает ресурсы согласно формуле")]
        public void UpdateProduction_IncreasesResources_AccordingToFormula()
        {
            // Arrange
            var colony = new Colony
            {
                Resources = new Resources
                {
                    VirtualResources = 100,
                    ProductionRate = 50,
                    ProductionBonus = 20 // +20%
                }
            };

            // Act
            _simulator.UpdateProduction(colony, deltaTime: 1.0f); // 1 секунда

            // Assert
            // resources += 50 * (1 + 20/100) * 1.0 = 50 * 1.2 = 60
            Assert.Equal(160f, colony.Resources.VirtualResources);
        }

        [Fact(DisplayName = "AddResourceNode - увеличивает бонус на 20%")]
        public void AddResourceNode_IncreasesBonus_By20Percent()
        {
            // Arrange
            var colony = new Colony { Resources = new Resources { ProductionBonus = 0 } };
            var node = new ResourceNode { Id = "node1", Type = ResourceNodeType.Iron };

            // Act
            _simulator.AddResourceNode(colony, node);

            // Assert
            Assert.Equal(20f, colony.Resources.ProductionBonus);
            Assert.Single(colony.ResourceNodes);
        }

        [Fact(DisplayName = "AddResourceNode - бонусы складываются")]
        public void AddResourceNode_BonusesStack()
        {
            // Arrange
            var colony = new Colony();
            var node1 = new ResourceNode { Id = "node1", Type = ResourceNodeType.Iron };
            var node2 = new ResourceNode { Id = "node2", Type = ResourceNodeType.Copper };

            // Act
            _simulator.AddResourceNode(colony, node1);
            _simulator.AddResourceNode(colony, node2);

            // Assert
            Assert.Equal(40f, colony.Resources.ProductionBonus);
            Assert.Equal(2, colony.ResourceNodes.Count);
        }

        [Fact(DisplayName = "RemoveResourceNode - уменьшает бонус на 20%")]
        public void RemoveResourceNode_DecreasesBonus_By20Percent()
        {
            // Arrange
            var colony = new Colony { Resources = new Resources { ProductionBonus = 40 } };
            var node = new ResourceNode { Id = "node1", Type = ResourceNodeType.Iron };
            colony.ResourceNodes.Add(node);

            // Act
            _simulator.RemoveResourceNode(colony, "node1");

            // Assert
            Assert.Equal(20f, colony.Resources.ProductionBonus);
            Assert.Empty(colony.ResourceNodes);
        }

        [Fact(DisplayName = "HasEnoughResourcesForUpgrade - возвращает true когда достаточно")]
        public void HasEnoughResourcesForUpgrade_ReturnsTrue_WhenEnough()
        {
            // Arrange
            var colony = new Colony
            {
                Stage = ColonyStage.ConstructionYard, // Следующая стадия: BaseL1, требуется 1000 ресурсов
                Resources = new Resources { VirtualResources = 1500 } // Есть 1500 (достаточно для BaseL1)
            };

            // Act
            var result = _simulator.HasEnoughResourcesForUpgrade(colony);

            // Assert
            Assert.True(result, "Should return true when resources (1500) >= required for BaseL1 (1000)");
        }

        [Fact(DisplayName = "HasEnoughResourcesForUpgrade - возвращает false когда недостаточно")]
        public void HasEnoughResourcesForUpgrade_ReturnsFalse_WhenNotEnough()
        {
            // Arrange
            var colony = new Colony
            {
                Stage = ColonyStage.ConstructionYard,
                Resources = new Resources { VirtualResources = 500 }
            };

            // Act
            var result = _simulator.HasEnoughResourcesForUpgrade(colony);

            // Assert
            Assert.False(result);
        }

        [Fact(DisplayName = "ConsumeResourcesForUpgrade - списывает ресурсы")]
        public void ConsumeResourcesForUpgrade_ConsumesResources()
        {
            // Arrange
            var colony = new Colony { Resources = new Resources { VirtualResources = 2000 } };

            // Act
            _simulator.ConsumeResourcesForUpgrade(colony, 1000);

            // Assert
            Assert.Equal(1000f, colony.Resources.VirtualResources);
        }

        [Fact(DisplayName = "GetTimeUntilNextUpgradeSeconds - возвращает 0 когда достаточно ресурсов")]
        public void GetTimeUntilNextUpgradeSeconds_ReturnsZero_WhenEnoughResources()
        {
            // Arrange
            var colony = new Colony { Resources = new Resources { VirtualResources = 2000, ProductionRate = 100 } };

            // Act
            var time = _simulator.GetTimeUntilNextUpgradeSeconds(colony, 1500);

            // Assert
            Assert.Equal(0f, time);
        }

        [Fact(DisplayName = "GetTimeUntilNextUpgradeSeconds - рассчитывает время корректно")]
        public void GetTimeUntilNextUpgradeSeconds_CalculatesTimeCorrectly()
        {
            // Arrange
            var colony = new Colony
            {
                Resources = new Resources
                {
                    VirtualResources = 1000,
                    ProductionRate = 100,
                    ProductionBonus = 0
                }
            };

            // Act
            var time = _simulator.GetTimeUntilNextUpgradeSeconds(colony, 2000);

            // Assert
            // Нехватка = 1000, скорость = 100/сек => 10 секунд
            Assert.Equal(10f, time);
        }
    }
}
