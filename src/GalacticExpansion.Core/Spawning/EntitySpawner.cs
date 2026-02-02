using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Gateway;
using GalacticExpansion.Core.Placement;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Spawning
{
    /// <summary>
    /// Реализация спавна структур и NPC в игре через Empyrion API.
    /// Координирует PlacementResolver для позиций и IEmpyrionGateway для запросов.
    /// </summary>
    public class EntitySpawner : IEntitySpawner
    {
        private readonly IEmpyrionGateway _gateway;
        private readonly IPlacementResolver _placementResolver;
        private readonly ILogger _logger;
        private const float NpcCircleRadius = 3f;
        private const int NpcSpawnDelayMs = 100;
        private const int StructureSpawnTimeoutMs = 10000;
        private const int NpcSpawnTimeoutMs = 5000;

        /// <summary>
        /// Создаёт спавнер сущностей с шлюзом к игре, резолвером размещения и логгером.
        /// </summary>
        /// <param name="gateway">Шлюз Empyrion API.</param>
        /// <param name="placementResolver">Резолвер позиций размещения.</param>
        /// <param name="logger">Логгер.</param>
        public EntitySpawner(IEmpyrionGateway gateway, IPlacementResolver placementResolver, ILogger logger)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _placementResolver = placementResolver ?? throw new ArgumentNullException(nameof(placementResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Спавнит структуру по имени префаба в заданной позиции с ротацией и фракцией.
        /// </summary>
        /// <param name="prefabName">Имя префаба структуры.</param>
        /// <param name="position">Позиция (X, Y, Z).</param>
        /// <param name="rotation">Ротация.</param>
        /// <param name="factionId">Идентификатор фракции.</param>
        /// <returns>EntityId созданной структуры.</returns>
        public async Task<int> SpawnStructureAsync(string prefabName, Vector3 position, Vector3 rotation, int factionId)
        {
            if (string.IsNullOrEmpty(prefabName))
                throw new ArgumentException("Prefab name cannot be empty", nameof(prefabName));

            _logger.Info($"Spawning structure '{prefabName}' at {position} (rotation={rotation}, faction={factionId})");

            try
            {
                var entityType = GetEntityTypeFromPrefab(prefabName);
                var spawnInfo = new EntitySpawnInfo
                {
                    prefabName = prefabName,
                    type = (byte)entityType,
                    pos = new PVector3(position.X, position.Y, position.Z),
                    rot = new PVector3(rotation.X, rotation.Y, rotation.Z),
                    factionGroup = (byte)factionId,
                    factionId = (byte)factionId
                };

                var entityId = await _gateway.SendRequestAsync<int>(CmdId.Request_Entity_Spawn, spawnInfo,
                    timeoutMs: StructureSpawnTimeoutMs);

                if (entityId > 0)
                {
                    _logger.Info($"✅ Structure '{prefabName}' spawned successfully (EntityId={entityId})");
                    return entityId;
                }

                throw new SpawnException($"Spawn returned invalid EntityId={entityId}", prefabName, position);
            }
            catch (TimeoutException)
            {
                _logger.Warn($"Timeout spawning '{prefabName}', retrying once...");

                try
                {
                    var entityType = GetEntityTypeFromPrefab(prefabName);
                    var spawnInfo = new EntitySpawnInfo
                    {
                        prefabName = prefabName,
                        type = (byte)entityType,
                        pos = new PVector3(position.X, position.Y, position.Z),
                        rot = new PVector3(rotation.X, rotation.Y, rotation.Z),
                        factionGroup = (byte)factionId,
                        factionId = (byte)factionId
                    };

                    var entityId = await _gateway.SendRequestAsync<int>(CmdId.Request_Entity_Spawn, spawnInfo,
                        timeoutMs: StructureSpawnTimeoutMs * 2);

                    if (entityId > 0)
                    {
                        _logger.Info($"✅ Structure '{prefabName}' spawned on retry (EntityId={entityId})");
                        return entityId;
                    }
                }
                catch { }

                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"❌ Error spawning structure '{prefabName}' at {position}");
                // Если ошибка уже содержит детали спавна, пробрасываем её без потери контекста.
                if (ex is SpawnException)
                    throw;

                // Для прочих ошибок создаем исключение с явным указанием prefab и позиции,
                // чтобы тесты и диагностика получали полную информацию.
                throw new SpawnException($"Failed to spawn '{prefabName}': {ex.Message}", prefabName, position);
            }
        }

        /// <summary>
        /// Спавнит структуру на рельефе: ищет высоту по (x, z) на playfield и вызывает SpawnStructureAsync.
        /// </summary>
        /// <param name="playfield">Название playfield.</param>
        /// <param name="prefabName">Имя префаба.</param>
        /// <param name="x">Координата X.</param>
        /// <param name="z">Координата Z.</param>
        /// <param name="factionId">Идентификатор фракции.</param>
        /// <param name="heightOffset">Отступ над землёй (метры).</param>
        /// <returns>EntityId созданной структуры.</returns>
        public async Task<int> SpawnStructureAtTerrainAsync(string playfield, string prefabName, float x, float z,
            int factionId, float heightOffset = 0.5f)
        {
            if (string.IsNullOrEmpty(prefabName))
                throw new ArgumentException("Prefab name cannot be empty", nameof(prefabName));
            if (string.IsNullOrEmpty(playfield))
                throw new ArgumentException("Playfield name cannot be empty", nameof(playfield));

            // ВАЖНО: порядок параметров соответствует интерфейсу IEntitySpawner,
            // чтобы playfield и prefabName не путались при вызовах из других модулей.
            _logger.Debug($"Finding terrain location for '{prefabName}' at ({x}, {z}) on '{playfield}'");

            try
            {
                var terrainPosition = await _placementResolver.FindLocationAtTerrainAsync(playfield, x, z, heightOffset);
                return await SpawnStructureAsync(prefabName, terrainPosition, new Vector3(), factionId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to spawn '{prefabName}' at terrain ({x}, {z})");
                throw;
            }
        }

        /// <summary>
        /// Спавнит группу NPC по кругу вокруг центральной позиции с задержкой между спавнами.
        /// </summary>
        /// <param name="playfield">Название playfield.</param>
        /// <param name="npcClassName">Класс NPC (например ZiraxMinigunPatrol).</param>
        /// <param name="centerPosition">Центр группы.</param>
        /// <param name="count">Количество NPC.</param>
        /// <param name="factionName">Имя фракции (например Zirax).</param>
        /// <returns>Список EntityId созданных NPC.</returns>
        public async Task<List<int>> SpawnNPCGroupAsync(string playfield, string npcClassName, Vector3 centerPosition,
            int count, string factionName)
        {
            if (count <= 0)
                throw new ArgumentException("Count must be positive", nameof(count));

            // Параметры идут в том же порядке, что и в интерфейсе:
            // сначала playfield, затем класс NPC, затем позиция и количество.
            _logger.Info($"Spawning {count}x '{npcClassName}' at {centerPosition} (faction={factionName})");

            var spawnedIds = new List<int>();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    float angle = (float)(2 * Math.PI * i / count);
                    float offsetX = NpcCircleRadius * (float)Math.Cos(angle);
                    float offsetZ = NpcCircleRadius * (float)Math.Sin(angle);

                    try
                    {
                        int entityId = await SpawnNPCAtTerrainAsync(
                            playfield,
                            npcClassName,
                            centerPosition.X + offsetX,
                            centerPosition.Z + offsetZ,
                            factionName);
                        spawnedIds.Add(entityId);

                        if (i < count - 1)
                            await Task.Delay(NpcSpawnDelayMs);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Failed to spawn NPC #{i + 1}");
                    }
                }

                _logger.Info($"✅ Spawned {spawnedIds.Count} NPC successfully");
                return spawnedIds;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error spawning NPC group (spawned {spawnedIds.Count}/{count})");
                throw;
            }
        }

        /// <summary>
        /// Спавнит одного NPC на рельефе в точке (x, z) на указанном playfield.
        /// </summary>
        /// <param name="playfield">Название playfield.</param>
        /// <param name="npcClassName">Класс NPC.</param>
        /// <param name="x">Координата X.</param>
        /// <param name="z">Координата Z.</param>
        /// <param name="factionName">Имя фракции.</param>
        /// <returns>EntityId созданного NPC.</returns>
        public async Task<int> SpawnNPCAtTerrainAsync(string playfield, string npcClassName, float x, float z,
            string factionName)
        {
            if (string.IsNullOrEmpty(npcClassName))
                throw new ArgumentException("NPC class name cannot be empty", nameof(npcClassName));

            try
            {
                var spawnPos = await _placementResolver.FindLocationAtTerrainAsync(playfield, x, z, 0.5f);

                var spawnInfo = new EntitySpawnInfo
                {
                    prefabName = npcClassName,
                    type = 8, // NPC type
                    pos = new PVector3(spawnPos.X, spawnPos.Y, spawnPos.Z),
                    rot = new PVector3(),
                    name = factionName
                };

                var entityId = await _gateway.SendRequestAsync<int>(CmdId.Request_Entity_Spawn, spawnInfo,
                    timeoutMs: NpcSpawnTimeoutMs);

                if (entityId > 0)
                {
                    _logger.Debug($"NPC '{npcClassName}' spawned at {spawnPos} (EntityId={entityId})");
                    return entityId;
                }

                throw new SpawnException($"NPC spawn returned invalid EntityId={entityId}", npcClassName, spawnPos);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to spawn NPC '{npcClassName}' at ({x}, {z})");
                throw;
            }
        }

        /// <summary>
        /// Уничтожает сущность по EntityId через API игры.
        /// </summary>
        /// <param name="entityId">Идентификатор сущности.</param>
        public async Task DestroyEntityAsync(int entityId)
        {
            if (entityId <= 0)
            {
                _logger.Warn($"Attempted to destroy invalid EntityId={entityId}");
                return;
            }

            _logger.Debug($"Destroying entity {entityId}");

            try
            {
                await _gateway.SendRequestAsync<object>(CmdId.Request_Entity_Destroy, new Id { id = entityId },
                    timeoutMs: 5000);
                _logger.Info($"✅ Entity {entityId} destroyed successfully");
            }
            catch (TimeoutException)
            {
                _logger.Warn($"Timeout destroying entity {entityId} (may already be destroyed)");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error destroying entity {entityId}");
            }
        }

        /// <summary>
        /// Уничтожает несколько сущностей по списку EntityId; ошибки по отдельным сущностям игнорируются.
        /// </summary>
        /// <param name="ids">Идентификаторы сущностей.</param>
        /// <returns>Количество успешно уничтоженных сущностей.</returns>
        public async Task<int> DestroyEntitiesAsync(IEnumerable<int> ids)
        {
            if (ids == null || !ids.Any())
                return 0;

            _logger.Info($"Destroying {ids.Count()} entities");

            int successCount = 0;
            foreach (var entityId in ids)
            {
                try
                {
                    await DestroyEntityAsync(entityId);
                    successCount++;
                }
                catch { }
            }

            _logger.Info($"Destroyed {successCount}/{ids.Count()} entities");
            return successCount;
        }

        /// <summary>
        /// Проверяет существование сущности по EntityId через запрос позиции/ротации.
        /// </summary>
        /// <param name="entityId">Идентификатор сущности.</param>
        /// <returns>True, если сущность существует.</returns>
        public async Task<bool> EntityExistsAsync(int entityId)
        {
            if (entityId <= 0)
                return false;

            try
            {
                var info = await _gateway.SendRequestAsync<EntityInfo>(CmdId.Request_Entity_PosAndRot, new Id { id = entityId },
                    timeoutMs: 3000);
                return info != null;
            }
            catch
            {
                return false;
            }
        }

        private int GetEntityTypeFromPrefab(string prefabName)
        {
            if (prefabName.Contains("BA-")) return 2; // Base
            if (prefabName.Contains("CV-")) return 6; // Capital Vessel
            if (prefabName.Contains("SV-")) return 8; // Small Vessel
            if (prefabName.Contains("HV-")) return 4; // Hover Vessel
            return 2; // Default
        }
    }
}
