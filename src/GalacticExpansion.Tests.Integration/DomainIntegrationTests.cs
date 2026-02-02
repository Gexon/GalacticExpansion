using System;
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

namespace GalacticExpansion.Tests.Integration
{
    /// <summary>
    /// Integration-тесты для полного жизненного цикла колонии Phase 3.
    /// Тестирование взаимодействия всех модулей: Economy, UnitEconomy, StageManager.
    /// </summary>
    public class DomainIntegrationTests
    {
        private readonly Mock<IEmpyrionGateway> _gatewayMock;
        private readonly Mock<IStateStore> _stateStoreMock;
        private readonly Mock<IEventBus> _eventBusMock;
        private readonly Mock<ILogger> _loggerMock;
        private readonly Configuration _config;

        public DomainIntegrationTests()
        {
            _gatewayMock = new Mock<IEmpyrionGateway>();
            _stateStoreMock = new Mock<IStateStore>();
            _eventBusMock = new Mock<IEventBus>();
            _loggerMock = new Mock<ILogger>();
            _config = CreateTestConfig();

            SetupGatewayMocks();
        }

        private void SetupGatewayMocks()
        {
            // Mock успешного спавна структур
            _gatewayMock.Setup(g => g.SendRequestAsync<int>(
                CmdId.Request_Entity_Spawn,
                It.IsAny<EntitySpawnInfo>(),
                It.IsAny<int>()))
                .ReturnsAsync(100);

            // Mock успешного удаления
            _gatewayMock.Setup(g => g.SendRequestAsync<object>(
                CmdId.Request_Entity_Destroy,
                It.IsAny<Id>(),
                It.IsAny<int>()))
                .ReturnsAsync(new object());

            // Mock Touch структур
            _gatewayMock.Setup(g => g.SendRequestAsync<object>(
                CmdId.Request_Structure_Touch,
                It.IsAny<Id>(),
                It.IsAny<int>()))
                .ReturnsAsync(new object());
        }

        private Configuration CreateTestConfig()
        {
            return new Configuration
            {
                Zirax = new ZiraxSettings
                {
                    FactionId = 2,
                    Stages = new System.Collections.Generic.List<StageConfig>
                    {
                        new StageConfig
                        {
                            Stage = "ConstructionYard",
                            PrefabName = "GLEX_ConstructionYard",
                            RequiredResources = 0,
                            ProductionRate = 100,
                            MinTimeSeconds = 60,
                            GuardCount = 2
                        },
                        new StageConfig
                        {
                            Stage = "BaseL1",
                            PrefabName = "GLEX_Base_L1",
                            RequiredResources = 1000,
                            ProductionRate = 150,
                            MinTimeSeconds = 180,
                            GuardCount = 6
                        },
                        new StageConfig
                        {
                            Stage = "BaseL2",
                            PrefabName = "GLEX_Base_L2",
                            RequiredResources = 3000,
                            ProductionRate = 250,
                            MinTimeSeconds = 300,
                            GuardCount = 10
                        }
                    }
                }
            };
        }

        [Fact(DisplayName = "Integration: Полный жизненный цикл колонии с апгрейдами")]
        public async Task ColonyLifecycle_FullCycle_WithUpgrades()
        {
            // Arrange - создание всех модулей
            var placementResolverMock = new Mock<IPlacementResolver>();
            placementResolverMock.Setup(pr => pr.FindLocationAtTerrainAsync(
                It.IsAny<string>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
                .ReturnsAsync(new Vector3(1000, 100, -500));

            var entitySpawner = new EntitySpawner(_gatewayMock.Object, placementResolverMock.Object, _loggerMock.Object);
            var economySimulator = new EconomySimulator(_config, _loggerMock.Object);
            var unitEconomy = new UnitEconomyManager(_config, _loggerMock.Object);

            var stageManager = new StageManager(
                _gatewayMock.Object,
                entitySpawner,
                placementResolverMock.Object,
                economySimulator,
                unitEconomy,
                _stateStoreMock.Object,
                _eventBusMock.Object,
                _config,
                _loggerMock.Object
            );

            // Act & Assert - Step 1: Создание колонии
            var colony = new Colony("Akua", 2, new Vector3(1000, 0, -500))
            {
                Stage = ColonyStage.ConstructionYard,
                MainStructureId = 100
            };
            colony.Resources.ProductionRate = 100;
            colony.UnitPool.MaxGuards = 2;
            colony.UnitPool.AvailableGuards = 2;

            Assert.Equal(ColonyStage.ConstructionYard, colony.Stage);
            Assert.Equal(0f, colony.Resources.VirtualResources);

            // Step 2: Производство ресурсов
            for (int i = 0; i < 10; i++)
            {
                economySimulator.UpdateProduction(colony, deltaTime: 1.0f);
            }

            Assert.Equal(1000f, colony.Resources.VirtualResources, precision: 1); // 100/сек * 10сек

            // Step 3: Проверка возможности апгрейда
            colony.LastUpgradeTime = DateTime.UtcNow.AddSeconds(-100); // Достаточно времени прошло
            var canUpgrade = await stageManager.CanTransitionToNextStageAsync(colony);
            Assert.True(canUpgrade);

            // Step 4: Апгрейд на BaseL1
            await stageManager.TransitionToNextStageAsync(colony);

            Assert.Equal(ColonyStage.BaseL1, colony.Stage);
            Assert.Equal(0f, colony.Resources.VirtualResources, precision: 1); // Потрачено 1000
            Assert.Equal(150f, colony.Resources.ProductionRate); // Новая скорость
            Assert.Equal(6, colony.UnitPool.MaxGuards); // Обновлено
        }

        [Fact(DisplayName = "Integration: Экономика с аванпостами ускоряет производство")]
        public void Economy_WithOutposts_AcceleratesProduction()
        {
            // Arrange
            var economySimulator = new EconomySimulator(_config, _loggerMock.Object);
            var colony = new Colony { Resources = new Resources { ProductionRate = 100 } };

            // Act - базовое производство
            economySimulator.UpdateProduction(colony, deltaTime: 10.0f);
            var baseProduction = colony.Resources.VirtualResources; // 1000

            // Добавление аванпоста (+20% бонус)
            colony.Resources.VirtualResources = 0;
            economySimulator.AddResourceNode(colony, new ResourceNode { Id = "outpost1", Type = ResourceNodeType.Iron });

            economySimulator.UpdateProduction(colony, deltaTime: 10.0f);
            var bonusProduction = colony.Resources.VirtualResources; // 1200

            // Assert
            Assert.Equal(1000f, baseProduction, precision: 1);
            Assert.Equal(1200f, bonusProduction, precision: 1);
            Assert.Equal(20f, colony.Resources.ProductionBonus);
        }

        [Fact(DisplayName = "Integration: Unit Economy с производством и потерями")]
        public void UnitEconomy_ProductionAndLosses_WorkCorrectly()
        {
            // Arrange
            var unitEconomy = new UnitEconomyManager(_config, _loggerMock.Object);
            var colony = new Colony
            {
                Stage = ColonyStage.BaseL1,
                UnitPool = new UnitPool
                {
                    ProductionRate = 3600, // 1/сек
                    MaxGuards = 6,
                    AvailableGuards = 0
                }
            };

            // Act - производство юнитов
            for (int i = 0; i < 6; i++)
            {
                unitEconomy.ProduceUnits(colony, deltaTime: 1.0f);
            }

            Assert.Equal(6, colony.UnitPool.AvailableGuards);

            // Резервирование и спавн
            var reserved = unitEconomy.ReserveUnits(colony, UnitType.Guard, count: 4);
            Assert.True(reserved);
            Assert.Equal(2, colony.UnitPool.AvailableGuards);

            // Регистрация активных
            unitEconomy.RegisterActiveUnit(colony, 101, UnitType.Guard, "BaseDefense");
            unitEconomy.RegisterActiveUnit(colony, 102, UnitType.Guard, "BaseDefense");

            Assert.Equal(2, colony.UnitPool.ActiveUnits.Count);

            // Потеря юнита
            unitEconomy.RecordUnitLoss(colony, entityId: 101);

            Assert.Single(colony.UnitPool.ActiveUnits);
        }

        [Fact(DisplayName = "Integration: Разрушение аванпоста снижает производство")]
        public void Economy_OutpostDestruction_ReducesProduction()
        {
            // Arrange
            var economySimulator = new EconomySimulator(_config, _loggerMock.Object);
            var unitEconomy = new UnitEconomyManager(_config, _loggerMock.Object);

            var colony = new Colony
            {
                Resources = new Resources { ProductionRate = 150 },
                UnitPool = new UnitPool { ProductionRate = 100 }
            };

            // Добавление 2 аванпостов
            economySimulator.AddResourceNode(colony, new ResourceNode { Id = "outpost1", Type = ResourceNodeType.Iron });
            economySimulator.AddResourceNode(colony, new ResourceNode { Id = "outpost2", Type = ResourceNodeType.Copper });

            Assert.Equal(40f, colony.Resources.ProductionBonus); // 2 * 20%

            // Act - разрушение одного аванпоста
            economySimulator.RemoveResourceNode(colony, "outpost1");
            unitEconomy.OnResourceOutpostDestroyed(colony);

            // Assert - бонус экономики уменьшен
            Assert.Equal(20f, colony.Resources.ProductionBonus);
            Assert.Single(colony.ResourceNodes);

            // ProductionRate юнитов снижен на 25%
            Assert.Equal(75f, colony.UnitPool.ProductionRate);
        }

        [Fact(DisplayName = "Integration: Расчет времени до апгрейда с учетом бонусов")]
        public void Economy_TimeToUpgrade_WithBonuses()
        {
            // Arrange
            var economySimulator = new EconomySimulator(_config, _loggerMock.Object);
            var colony = new Colony
            {
                Stage = ColonyStage.ConstructionYard,
                Resources = new Resources
                {
                    VirtualResources = 500,
                    ProductionRate = 100,
                    ProductionBonus = 0
                }
            };

            // Act - базовое время
            var baseTime = economySimulator.GetTimeUntilNextUpgradeSeconds(colony, requiredResources: 1000);

            // Добавление аванпоста (+20%)
            economySimulator.AddResourceNode(colony, new ResourceNode { Id = "outpost1", Type = ResourceNodeType.Iron });
            var bonusTime = economySimulator.GetTimeUntilNextUpgradeSeconds(colony, requiredResources: 1000);

            // Assert
            Assert.Equal(5f, baseTime); // (1000 - 500) / 100 = 5 сек
            Assert.InRange(bonusTime, 4.1f, 4.2f); // (1000 - 500) / (100 * 1.2) ≈ 4.17 сек
        }

        [Fact(DisplayName = "Integration: ColonyManager координирует все модули")]
        public async Task ColonyManager_CoordinatesAllModules()
        {
            // Arrange
            var placementResolverMock = new Mock<IPlacementResolver>();
            placementResolverMock.Setup(pr => pr.FindLocationAtTerrainAsync(
                It.IsAny<string>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
                .ReturnsAsync(new Vector3(1000, 100, -500));

            var entitySpawner = new EntitySpawner(_gatewayMock.Object, placementResolverMock.Object, _loggerMock.Object);
            var economySimulator = new EconomySimulator(_config, _loggerMock.Object);
            var unitEconomy = new UnitEconomyManager(_config, _loggerMock.Object);

            var stageManager = new StageManager(
                _gatewayMock.Object,
                entitySpawner,
                placementResolverMock.Object,
                economySimulator,
                unitEconomy,
                _stateStoreMock.Object,
                _eventBusMock.Object,
                _config,
                _loggerMock.Object
            );

            var colonyManager = new ColonyManager(
                stageManager,
                economySimulator,
                unitEconomy,
                _stateStoreMock.Object,
                _loggerMock.Object
            );

            var colony = new Colony("Akua", 2, new Vector3(1000, 0, -500))
            {
                Stage = ColonyStage.ConstructionYard,
                MainStructureId = 100
            };
            colony.Resources.ProductionRate = 100;
            colony.UnitPool.ProductionRate = 3600;
            colony.UnitPool.MaxGuards = 2;

            // Act - обновление колонии несколько раз
            for (int i = 0; i < 5; i++)
            {
                await colonyManager.UpdateColonyAsync(colony, deltaTime: 1.0f);
            }

            // Assert - ресурсы произведены
            Assert.True(colony.Resources.VirtualResources > 0);

            // Юниты частично произведены
            Assert.True(colony.UnitPool.ProductionProgress > 0 || colony.UnitPool.AvailableGuards > 0);
        }
    }
}
