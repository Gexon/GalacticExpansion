using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Gateway;
using GalacticExpansion.Core.Placement;
using GalacticExpansion.Core.Spawning;
using GalacticExpansion.Models;
using Moq;
using NLog;
using Xunit;
using CoreEntityInfo = GalacticExpansion.Core.Spawning.EntityInfo;

namespace GalacticExpansion.Tests.Unit.Spawning
{
    /// <summary>
    /// Unit-тесты для EntitySpawner - модуля создания и удаления игровых сущностей.
    /// Тестирование:
    /// - Спавна структур с валидацией
    /// - Спавна на рельефе с точным определением высоты
    /// - Спавна группы NPC по кругу
    /// - Удаления сущностей
    /// - Обработки ошибок и retry логики
    /// </summary>
    public class EntitySpawnerTests
    {
        private readonly Mock<IEmpyrionGateway> _gatewayMock;
        private readonly Mock<IPlacementResolver> _placementResolverMock;
        private readonly Mock<ILogger> _loggerMock;
        private readonly EntitySpawner _spawner;

        public EntitySpawnerTests()
        {
            _gatewayMock = new Mock<IEmpyrionGateway>();
            _placementResolverMock = new Mock<IPlacementResolver>();
            _loggerMock = new Mock<ILogger>();

            // По умолчанию успешный спавн структуры
            _gatewayMock
                .Setup(g => g.SendRequestAsync<int>(
                    CmdId.Request_Entity_Spawn,
                    It.IsAny<EntitySpawnInfo>(),
                    It.IsAny<int>()))
                .ReturnsAsync(12345); // Валидный EntityId

            // По умолчанию успешное определение высоты
            _placementResolverMock
                .Setup(pr => pr.FindLocationAtTerrainAsync(
                    It.IsAny<string>(),
                    It.IsAny<float>(),
                    It.IsAny<float>(),
                    It.IsAny<float>()))
                .ReturnsAsync(new Vector3(100, 50, 200));

            _spawner = new EntitySpawner(_gatewayMock.Object, _placementResolverMock.Object, _loggerMock.Object);
        }

        [Fact(DisplayName = "Конструктор - выбрасывает ArgumentNullException при null параметрах")]
        public void Constructor_ThrowsArgumentNullException_WhenParametersAreNull()
        {
            // Assert
            Assert.Throws<ArgumentNullException>(() => new EntitySpawner(null!, _placementResolverMock.Object, _loggerMock.Object));
            Assert.Throws<ArgumentNullException>(() => new EntitySpawner(_gatewayMock.Object, null!, _loggerMock.Object));
            Assert.Throws<ArgumentNullException>(() => new EntitySpawner(_gatewayMock.Object, _placementResolverMock.Object, null!));
        }

        [Fact(DisplayName = "SpawnStructureAsync - успешно спавнит структуру")]
        public async Task SpawnStructureAsync_SpawnsStructure_Successfully()
        {
            // Arrange
            var prefabName = "GLEX_Base_L1";
            var position = new Vector3(1000, 100, -500);
            var rotation = new Vector3();
            var factionId = 2;

            // Act
            var entityId = await _spawner.SpawnStructureAsync(prefabName, position, rotation, factionId);

            // Assert
            Assert.Equal(12345, entityId);
            _gatewayMock.Verify(
                g => g.SendRequestAsync<int>(
                    CmdId.Request_Entity_Spawn,
                    It.Is<EntitySpawnInfo>(info =>
                        info.prefabName == prefabName &&
                        info.factionId == factionId),
                    It.IsAny<int>()),
                Times.Once);
        }

        [Fact(DisplayName = "SpawnStructureAsync - выбрасывает ArgumentException для пустого prefab")]
        public async Task SpawnStructureAsync_ThrowsArgumentException_WhenPrefabNameIsEmpty()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                () => _spawner.SpawnStructureAsync("", new Vector3(), new Vector3(), 2)
            );
        }

        [Fact(DisplayName = "SpawnStructureAsync - выбрасывает SpawnException когда EntityId <= 0")]
        public async Task SpawnStructureAsync_ThrowsSpawnException_WhenEntityIdIsInvalid()
        {
            // Arrange
            _gatewayMock
                .Setup(g => g.SendRequestAsync<int>(
                    CmdId.Request_Entity_Spawn,
                    It.IsAny<EntitySpawnInfo>(),
                    It.IsAny<int>()))
                .ReturnsAsync(0); // Невалидный EntityId

            // Act & Assert
            var exception = await Assert.ThrowsAsync<SpawnException>(
                () => _spawner.SpawnStructureAsync("TestPrefab", new Vector3(), new Vector3(), 2)
            );

            Assert.Contains("invalid EntityId", exception.Message);
            Assert.Equal("TestPrefab", exception.PrefabName);
        }

        [Fact(DisplayName = "SpawnStructureAsync - делает retry при TimeoutException")]
        public async Task SpawnStructureAsync_RetriesOnce_WhenTimeoutOccurs()
        {
            // Arrange
            var callCount = 0;
            _gatewayMock
                .Setup(g => g.SendRequestAsync<int>(
                    CmdId.Request_Entity_Spawn,
                    It.IsAny<EntitySpawnInfo>(),
                    It.IsAny<int>()))
                .Returns(() =>
                {
                    callCount++;
                    if (callCount == 1)
                        throw new TimeoutException("First attempt timeout");
                    return Task.FromResult(12345); // Успех на retry
                });

            // Act
            var entityId = await _spawner.SpawnStructureAsync("TestPrefab", new Vector3(), new Vector3(), 2);

            // Assert
            Assert.Equal(12345, entityId);
            Assert.Equal(2, callCount); // Первая попытка + retry
        }

        [Fact(DisplayName = "SpawnStructureAtTerrainAsync - использует PlacementResolver для высоты")]
        public async Task SpawnStructureAtTerrainAsync_UsesPlacementResolver_ForTerrainHeight()
        {
            // Arrange
            var playfield = "Akua";
            var prefabName = "GLEX_Base_L2";
            var x = 500f;
            var z = -300f;
            var factionId = 2;
            var heightOffset = 1.0f;

            var expectedPosition = new Vector3(500, 75, -300);
            _placementResolverMock
                .Setup(pr => pr.FindLocationAtTerrainAsync(playfield, x, z, heightOffset))
                .ReturnsAsync(expectedPosition);

            // Act
            var entityId = await _spawner.SpawnStructureAtTerrainAsync(
                playfield,
                prefabName,
                x,
                z,
                factionId,
                heightOffset
            );

            // Assert
            Assert.Equal(12345, entityId);
            _placementResolverMock.Verify(
                pr => pr.FindLocationAtTerrainAsync(playfield, x, z, heightOffset),
                Times.Once);
        }

        [Fact(DisplayName = "SpawnNPCGroupAsync - спавнит несколько NPC по кругу")]
        public async Task SpawnNPCGroupAsync_SpawnsMultipleNPCs_InCircle()
        {
            // Arrange
            var playfield = "Akua";
            var npcClassName = "ZiraxMinigunPatrol";
            var centerPosition = new Vector3(100, 50, 200);
            var count = 4;
            var factionName = "Zirax";

            var spawnedEntityIds = new List<int> { 101, 102, 103, 104 };
            var callIndex = 0;

            _gatewayMock
                .Setup(g => g.SendRequestAsync<int>(
                    CmdId.Request_Entity_Spawn,
                    It.IsAny<EntitySpawnInfo>(),
                    It.IsAny<int>()))
                .Returns(() => Task.FromResult(spawnedEntityIds[callIndex++]));

            // Act
            var result = await _spawner.SpawnNPCGroupAsync(
                playfield,
                npcClassName,
                centerPosition,
                count,
                factionName
            );

            // Assert
            Assert.Equal(count, result.Count);
            Assert.Equal(spawnedEntityIds, result);
            _gatewayMock.Verify(
                g => g.SendRequestAsync<int>(
                    CmdId.Request_Entity_Spawn,
                    It.IsAny<EntitySpawnInfo>(),
                    It.IsAny<int>()),
                Times.Exactly(count));
        }

        [Fact(DisplayName = "SpawnNPCGroupAsync - выбрасывает ArgumentException для count <= 0")]
        public async Task SpawnNPCGroupAsync_ThrowsArgumentException_WhenCountIsInvalid()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                () => _spawner.SpawnNPCGroupAsync("Akua", "TestNPC", new Vector3(), 0, "Zirax")
            );

            await Assert.ThrowsAsync<ArgumentException>(
                () => _spawner.SpawnNPCGroupAsync("Akua", "TestNPC", new Vector3(), -1, "Zirax")
            );
        }

        [Fact(DisplayName = "DestroyEntityAsync - успешно удаляет сущность")]
        public async Task DestroyEntityAsync_DestroysEntity_Successfully()
        {
            // Arrange
            var entityId = 123;

            // Act
            await _spawner.DestroyEntityAsync(entityId);

            // Assert
            _gatewayMock.Verify(
                g => g.SendRequestAsync<object>(
                    CmdId.Request_Entity_Destroy,
                    It.Is<Id>(id => id.id == entityId),
                    It.IsAny<int>()),
                Times.Once);
        }

        [Fact(DisplayName = "DestroyEntityAsync - игнорирует невалидный EntityId")]
        public async Task DestroyEntityAsync_Ignores_InvalidEntityId()
        {
            // Act
            await _spawner.DestroyEntityAsync(0);
            await _spawner.DestroyEntityAsync(-1);

            // Assert
            _gatewayMock.Verify(
                g => g.SendRequestAsync<object>(
                    CmdId.Request_Entity_Destroy,
                    It.IsAny<Id>(),
                    It.IsAny<int>()),
                Times.Never);
        }

        [Fact(DisplayName = "DestroyEntitiesAsync - удаляет несколько сущностей")]
        public async Task DestroyEntitiesAsync_DestroysMultipleEntities_Successfully()
        {
            // Arrange
            var entityIds = new List<int> { 100, 101, 102 };

            // Act
            var successCount = await _spawner.DestroyEntitiesAsync(entityIds);

            // Assert
            Assert.Equal(3, successCount);
            _gatewayMock.Verify(
                g => g.SendRequestAsync<object>(
                    CmdId.Request_Entity_Destroy,
                    It.IsAny<Id>(),
                    It.IsAny<int>()),
                Times.Exactly(3));
        }

        [Fact(DisplayName = "DestroyEntitiesAsync - возвращает 0 для пустого списка")]
        public async Task DestroyEntitiesAsync_ReturnsZero_ForEmptyList()
        {
            // Act
            var successCount = await _spawner.DestroyEntitiesAsync(new List<int>());

            // Assert
            Assert.Equal(0, successCount);
        }

        [Fact(DisplayName = "EntityExistsAsync - возвращает true для существующей сущности")]
        public async Task EntityExistsAsync_ReturnsTrue_WhenEntityExists()
        {
            // Arrange
            // Возвращаем EntityInfo, потому что метод EntityExistsAsync ожидает именно этот тип из gateway.
            _gatewayMock
                .Setup(g => g.SendRequestAsync<CoreEntityInfo>(
                    CmdId.Request_Entity_PosAndRot,
                    It.IsAny<Id>(),
                    It.IsAny<int>()))
                .ReturnsAsync(new CoreEntityInfo());

            // Act
            var exists = await _spawner.EntityExistsAsync(123);

            // Assert
            Assert.True(exists);
        }

        [Fact(DisplayName = "EntityExistsAsync - возвращает false для несуществующей сущности")]
        public async Task EntityExistsAsync_ReturnsFalse_WhenEntityDoesNotExist()
        {
            // Arrange
            // Исключение имитирует отсутствие сущности в ModAPI (например, сущность удалена).
            _gatewayMock
                .Setup(g => g.SendRequestAsync<CoreEntityInfo>(
                    CmdId.Request_Entity_PosAndRot,
                    It.IsAny<Id>(),
                    It.IsAny<int>()))
                .ThrowsAsync(new Exception("Entity not found"));

            // Act
            var exists = await _spawner.EntityExistsAsync(999);

            // Assert
            Assert.False(exists);
        }

        [Fact(DisplayName = "EntityExistsAsync - возвращает false для невалидного EntityId")]
        public async Task EntityExistsAsync_ReturnsFalse_ForInvalidEntityId()
        {
            // Act
            var exists1 = await _spawner.EntityExistsAsync(0);
            var exists2 = await _spawner.EntityExistsAsync(-1);

            // Assert
            Assert.False(exists1);
            Assert.False(exists2);
        }
    }
}
