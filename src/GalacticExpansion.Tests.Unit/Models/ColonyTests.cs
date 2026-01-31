using System;
using GalacticExpansion.Models;
using Xunit;

namespace GalacticExpansion.Tests.Unit.Models
{
    /// <summary>
    /// Unit-тесты для модели Colony.
    /// Проверяют бизнес-логику колонии: улучшения, откаты, ресурсы.
    /// </summary>
    public class ColonyTests
    {
        [Fact]
        public void Constructor_InitializesCorrectly()
        {
            // Arrange & Act
            var colony = new Colony("Akua", 2, new Vector3(1000, 150, -500));

            // Assert
            Assert.Equal("Akua", colony.Playfield);
            Assert.Equal(2, colony.FactionId);
            Assert.Equal(1000, colony.Position.X);
            Assert.Equal(ColonyStage.LandingPending, colony.Stage);
            Assert.Equal(1, colony.ThreatLevel);
            Assert.NotNull(colony.Resources);
            Assert.NotNull(colony.UnitPool);
            Assert.Empty(colony.ResourceNodes);
            Assert.Empty(colony.DestructionEvents);
        }

        [Fact]
        public void CanUpgrade_ReturnsFalseWhenInsufficientResources()
        {
            // Arrange
            var colony = new Colony("Akua", 2, new Vector3(1000, 150, -500))
            {
                Stage = ColonyStage.ConstructionYard
            };
            colony.Resources.VirtualResources = 500;

            // Act
            var canUpgrade = colony.CanUpgrade(requiredResources: 1000, minTimeSeconds: 0);

            // Assert
            Assert.False(canUpgrade);
        }

        [Fact]
        public void CanUpgrade_ReturnsTrueWhenRequirementsMet()
        {
            // Arrange
            var colony = new Colony("Akua", 2, new Vector3(1000, 150, -500))
            {
                Stage = ColonyStage.ConstructionYard
            };
            colony.Resources.VirtualResources = 2000;

            // Act
            var canUpgrade = colony.CanUpgrade(requiredResources: 1000, minTimeSeconds: 0);

            // Assert
            Assert.True(canUpgrade);
        }

        [Fact]
        public void CanUpgrade_ReturnsFalseWhenMinTimeNotPassed()
        {
            // Arrange
            var colony = new Colony("Akua", 2, new Vector3(1000, 150, -500))
            {
                Stage = ColonyStage.ConstructionYard,
                LastUpgradeTime = DateTime.UtcNow
            };
            colony.Resources.VirtualResources = 2000;

            // Act
            var canUpgrade = colony.CanUpgrade(requiredResources: 1000, minTimeSeconds: 3600);

            // Assert
            Assert.False(canUpgrade);
        }

        [Fact]
        public void Upgrade_AdvancesStageAndConsumesResources()
        {
            // Arrange
            var colony = new Colony("Akua", 2, new Vector3(1000, 150, -500))
            {
                Stage = ColonyStage.ConstructionYard
            };
            colony.Resources.VirtualResources = 2000;

            // Act
            colony.Upgrade(resourceCost: 1000);

            // Assert
            Assert.Equal(ColonyStage.BaseL1, colony.Stage);
            Assert.Equal(1000, colony.Resources.VirtualResources);
            Assert.NotNull(colony.LastUpgradeTime);
        }

        [Fact]
        public void Downgrade_ReturnsToPreviousStage()
        {
            // Arrange
            var colony = new Colony("Akua", 2, new Vector3(1000, 150, -500))
            {
                Stage = ColonyStage.BaseL2,
                MainStructureId = 12345
            };

            // Act
            var downgraded = colony.Downgrade();

            // Assert
            Assert.True(downgraded);
            Assert.Equal(ColonyStage.BaseL1, colony.Stage);
            Assert.Null(colony.MainStructureId); // Структура была разрушена
        }

        [Fact]
        public void Downgrade_ReturnsFalseAtMinimumStage()
        {
            // Arrange
            var colony = new Colony("Akua", 2, new Vector3(1000, 150, -500))
            {
                Stage = ColonyStage.ConstructionYard
            };

            // Act
            var downgraded = colony.Downgrade();

            // Assert
            Assert.False(downgraded);
            Assert.Equal(ColonyStage.ConstructionYard, colony.Stage);
        }

        [Fact]
        public void RecordDestruction_IncreaseThreatLevel()
        {
            // Arrange
            var colony = new Colony("Akua", 2, new Vector3(1000, 150, -500))
            {
                ThreatLevel = 2
            };

            // Act
            colony.RecordDestruction(entityId: 12345, type: "ResourceNode", destroyedBy: 100);

            // Assert
            Assert.Equal(3, colony.ThreatLevel);
            Assert.Single(colony.DestructionEvents);
            Assert.Equal(12345, colony.DestructionEvents[0].DestroyedEntityId);
            Assert.Equal("ResourceNode", colony.DestructionEvents[0].DestroyedType);
            Assert.Equal(100, colony.DestructionEvents[0].DestroyedBy);
            Assert.NotNull(colony.LastAttackTime);
        }

        [Fact]
        public void RecordDestruction_ThreatLevelCappedAtFive()
        {
            // Arrange
            var colony = new Colony("Akua", 2, new Vector3(1000, 150, -500))
            {
                ThreatLevel = 5
            };

            // Act
            colony.RecordDestruction(entityId: 12345, type: "Base", destroyedBy: 100);

            // Assert
            Assert.Equal(5, colony.ThreatLevel); // Не должен превысить 5
        }
    }
}
