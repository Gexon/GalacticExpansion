using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.IPC;
using GalacticExpansion.Core.Placement;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Spawning
{
    /// <summary>
    /// IPC-aware EntitySpawner для правильной работы в multi-process архитектуре Empyrion.
    /// 
    /// АРХИТЕКТУРНОЕ ПРАВИЛО:
    /// - Dedi процесс НЕ имеет _directSpawner! Спавн через ModAPI Gateway на Dedi не работает
    ///   (Empyrion отвечает Event_Ok без entity ID). Все spawn-операции на Dedi идут ТОЛЬКО через IPC к PfServer.
    /// - PfServer процесс использует _directSpawner для прямого spawn через ModAPI.
    /// 
    /// На Dedi доступен только IPlacementResolver (определение terrain height) и NetworkBridge (IPC).
    /// EntitySpawner на Dedi намеренно отсутствует, чтобы исключить случайный вызов спавна через Gateway.
    /// </summary>
    public class IPCEntitySpawner : IEntitySpawner
    {
        // На Dedi = null! Спавн через ModAPI Gateway на Dedi запрещён архитектурно.
        private readonly IEntitySpawner? _directSpawner;
        private readonly NetworkBridge? _networkBridge; // Для Dedi процесса (IPC к PfServer)
        private readonly IPlacementResolver? _placementResolver; // Для Dedi: определение terrain height
        private readonly ApplicationMode _processMode;
        private readonly ILogger _logger;

        /// <summary>
        /// Конструктор для Dedi процесса: IPC через NetworkBridge, terrain height через PlacementResolver.
        /// EntitySpawner НЕ передаётся — спавн через Gateway на Dedi запрещён.
        /// </summary>
        public IPCEntitySpawner(
            NetworkBridge networkBridge,
            IPlacementResolver placementResolver,
            ApplicationMode processMode,
            ILogger logger)
        {
            _networkBridge = networkBridge ?? throw new ArgumentNullException(nameof(networkBridge));
            _placementResolver = placementResolver ?? throw new ArgumentNullException(nameof(placementResolver));
            _directSpawner = null; // Намеренно null — спавн через Gateway на Dedi не работает
            _processMode = processMode;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.Info($"IPCEntitySpawner initialized for {_processMode} mode (IPC only, no direct spawner)");
        }

        /// <summary>
        /// Конструктор для PfServer процесса: прямой spawn через EntitySpawner, без NetworkBridge.
        /// </summary>
        public IPCEntitySpawner(
            IEntitySpawner directSpawner,
            ApplicationMode processMode,
            ILogger logger)
        {
            _directSpawner = directSpawner ?? throw new ArgumentNullException(nameof(directSpawner));
            _networkBridge = null;
            _placementResolver = null;
            _processMode = processMode;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.Info($"IPCEntitySpawner initialized for {_processMode} mode (direct spawn)");
        }

        /// <summary>
        /// Спавнит структуру на указанном playfield.
        /// Автоматически выбирает между IPC (Dedi) и прямым spawn (PfServer).
        /// </summary>
        public async Task<int> SpawnStructureAsync(string playfield, string prefabName, Vector3 position, Vector3 rotation, int factionId)
        {
            if (string.IsNullOrEmpty(playfield))
                throw new ArgumentException("Playfield cannot be empty", nameof(playfield));
            if (string.IsNullOrEmpty(prefabName))
                throw new ArgumentException("Prefab name cannot be empty", nameof(prefabName));

            // Выбираем метод спавна в зависимости от процесса
            if (_processMode == ApplicationMode.DedicatedServer)
            {
                // Абсолютная позиция: PfServer не пересчитывает Y.
                return await SpawnViaIPCAsync(playfield, prefabName, position, rotation, factionId, snapToTerrain: false, heightOffset: 0.5f);
            }
            else if (_processMode == ApplicationMode.PlayfieldServer)
            {
                if (_directSpawner == null)
                    throw new InvalidOperationException("DirectSpawner not available on PfServer — initialization error");

                _logger.Debug($"[PfServer] Direct spawn: {prefabName} on {playfield}");
                return await _directSpawner.SpawnStructureAsync(playfield, prefabName, position, rotation, factionId);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported process mode for spawn: {_processMode}");
            }
        }

        /// <summary>
        /// Спавн через IPC (Dedi -> PfServer).
        /// Отправляет SpawnStructureRequest и ждет SpawnStructureResponse.
        /// snapToTerrain: true — PfServer заменит Y высотой рельефа. Абсолютный спавн передаёт false.
        /// heightOffset учитывается только при snapToTerrain.
        /// </summary>
        private async Task<int> SpawnViaIPCAsync(
            string playfield,
            string prefabName,
            Vector3 position,
            Vector3 rotation,
            int factionId,
            bool snapToTerrain,
            float heightOffset)
        {
            if (_networkBridge == null)
                throw new InvalidOperationException("NetworkBridge not initialized for Dedi process");

            _logger.Info($"[Dedi] Sending IPC spawn request: {prefabName} on {playfield} (snap={snapToTerrain}, offset={heightOffset})");

            // Определяем тип entity из префаба
            var entityType = GetEntityTypeFromPrefab(prefabName);

            // Создаем IPC запрос.
            // snap=false оставляет присланную Y. snap=true просит PfServer посчитать землю сам.
            var request = new SpawnStructureRequest
            {
                RequestId = Guid.NewGuid(),
                Playfield = playfield,
                PrefabName = prefabName,
                Position = new[] { position.X, position.Y, position.Z },
                Rotation = new[] { rotation.X, rotation.Y, rotation.Z },
                FactionId = factionId,
                EntityType = (byte)entityType,
                SnapToTerrain = snapToTerrain,
                HeightOffset = heightOffset
            };

            try
            {
                // Отправляем запрос и ждем ответ (15 секунд таймаут)
                var response = await _networkBridge.SendRequestToPlayfieldAsync<SpawnStructureResponse>(request, playfield, timeoutMs: 15000);

                if (response.Success)
                {
                    _logger.Info($"[Dedi] ✅ IPC spawn successful: EntityId={response.EntityId}");
                    return response.EntityId;
                }
                else
                {
                    var errorMsg = $"PfServer spawn failed: {response.ErrorMessage}";
                    _logger.Error($"[Dedi] ❌ {errorMsg}");
                    throw new SpawnException(errorMsg, prefabName, position);
                }
            }
            catch (TimeoutException)
            {
                var errorMsg = $"IPC spawn timeout after 15s (playfield: {playfield})";
                _logger.Error($"[Dedi] ❌ {errorMsg}");
                throw new SpawnException(errorMsg, prefabName, position);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[Dedi] ❌ IPC spawn error: {prefabName}");
                throw new SpawnException($"IPC spawn failed: {ex.Message}", prefabName, position);
            }
        }

        /// <summary>
        /// Спавнит структуру на рельефе (находит высоту автоматически).
        /// На Dedi рельефа нет: в IPC уходят только X/Z, флаг SnapToTerrain и отступ.
        /// Высоту считает PfServer через IPlayfield.GetTerrainHeightAt (Module_04 §3.2, Module_06 §4).
        /// На PfServer: делегируем к _directSpawner (прямой spawn через ModAPI).
        /// </summary>
        public async Task<int> SpawnStructureAtTerrainAsync(string playfield, string prefabName, float x, float z, int factionId, float heightOffset = 0.5f)
        {
            if (_processMode == ApplicationMode.DedicatedServer)
            {
                // Конструктор Dedi требует PlacementResolver, но Y с dedicated не финальная:
                // кэш playfield там пуст, и запасные 100 м поднимают базу в воздух.
                if (_placementResolver == null)
                    throw new InvalidOperationException("PlacementResolver not available on Dedi — initialization error");

                _logger.Debug($"[Dedi] Requesting terrain snap for '{prefabName}' at ({x}, {z}) on '{playfield}', offset={heightOffset}m");

                // Y в пакете не используется: PfServer подставит высоту земли.
                return await SpawnViaIPCAsync(
                    playfield,
                    prefabName,
                    new Vector3(x, 0f, z),
                    new Vector3(0, 0, 0),
                    factionId,
                    snapToTerrain: true,
                    heightOffset: heightOffset);
            }
            else if (_processMode == ApplicationMode.PlayfieldServer)
            {
                if (_directSpawner == null)
                    throw new InvalidOperationException("DirectSpawner not available on PfServer — initialization error");

                return await _directSpawner.SpawnStructureAtTerrainAsync(playfield, prefabName, x, z, factionId, heightOffset);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported process mode: {_processMode}");
            }
        }

        /// <summary>
        /// Спавнит группу NPC по кругу.
        /// </summary>
        public async Task<List<int>> SpawnNPCGroupAsync(string playfield, string npcClassName, Vector3 centerPosition, int count, string factionName)
        {
            if (count <= 0)
                throw new ArgumentException("Count must be positive", nameof(count));

            _logger.Info($"Spawning {count}x '{npcClassName}' NPC group at {centerPosition} (faction={factionName})");

            var spawnedIds = new List<int>();
            const float circleRadius = 3.0f; // Радиус круга для NPC

            for (int i = 0; i < count; i++)
            {
                float angle = (float)(2 * Math.PI * i / count);
                float offsetX = circleRadius * (float)Math.Cos(angle);
                float offsetZ = circleRadius * (float)Math.Sin(angle);

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
                        await Task.Delay(500); // Задержка между спавнами NPC
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to spawn NPC #{i + 1}");
                }
            }

            _logger.Info($"✅ Spawned {spawnedIds.Count} NPC successfully");
            return spawnedIds;
        }

        /// <summary>
        /// Спавнит одного NPC на рельефе.
        /// Автоматически выбирает между IPC (Dedi) и прямым spawn (PfServer).
        /// </summary>
        public async Task<int> SpawnNPCAtTerrainAsync(string playfield, string npcClassName, float x, float z, string factionName)
        {
            if (string.IsNullOrEmpty(playfield))
                throw new ArgumentException("Playfield cannot be empty", nameof(playfield));
            if (string.IsNullOrEmpty(npcClassName))
                throw new ArgumentException("NPC class name cannot be empty", nameof(npcClassName));

            // Выбираем метод спавна в зависимости от процесса
            if (_processMode == ApplicationMode.DedicatedServer)
            {
                // ==== DEDI ПРОЦЕСС: Отправляем IPC команду в PfServer ====
                return await SpawnNPCViaIPCAsync(playfield, npcClassName, x, z, factionName);
            }
            else if (_processMode == ApplicationMode.PlayfieldServer)
            {
                if (_directSpawner == null)
                    throw new InvalidOperationException("DirectSpawner not available on PfServer — initialization error");

                _logger.Debug($"[PfServer] Direct NPC spawn: {npcClassName} on {playfield}");
                return await _directSpawner.SpawnNPCAtTerrainAsync(playfield, npcClassName, x, z, factionName);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported process mode for NPC spawn: {_processMode}");
            }
        }

        /// <summary>
        /// Спавн NPC через IPC (Dedi -> PfServer).
        /// Отправляет SpawnNPCRequest и ждет SpawnNPCResponse.
        /// </summary>
        private async Task<int> SpawnNPCViaIPCAsync(string playfield, string npcClassName, float x, float z, string factionName)
        {
            if (_networkBridge == null)
                throw new InvalidOperationException("NetworkBridge not initialized for Dedi process");

            _logger.Info($"[Dedi] Sending IPC NPC spawn request: {npcClassName} on {playfield}");

            // Создаем IPC запрос (позиция будет определена в PfServer через terrain height)
            var request = new SpawnNPCRequest
            {
                RequestId = Guid.NewGuid(),
                Playfield = playfield,
                NPCClassName = npcClassName,
                Position = new[] { x, 0f, z }, // Y будет определена в PfServer
                Rotation = new[] { 0f, 0f, 0f },
                FactionName = factionName
            };

            try
            {
                // Отправляем запрос и ждем ответ (10 секунд таймаут для NPC)
                var response = await _networkBridge.SendRequestToPlayfieldAsync<SpawnNPCResponse>(request, playfield, timeoutMs: 10000);

                if (response.Success)
                {
                    _logger.Info($"[Dedi] ✅ IPC NPC spawn successful: EntityId={response.EntityId}");
                    return response.EntityId;
                }
                else
                {
                    var errorMsg = $"PfServer NPC spawn failed: {response.ErrorMessage}";
                    _logger.Error($"[Dedi] ❌ {errorMsg}");
                    throw new SpawnException(errorMsg, npcClassName, new Vector3(x, 0, z));
                }
            }
            catch (TimeoutException)
            {
                var errorMsg = $"IPC NPC spawn timeout after 10s (playfield: {playfield})";
                _logger.Error($"[Dedi] ❌ {errorMsg}");
                throw new SpawnException(errorMsg, npcClassName, new Vector3(x, 0, z));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[Dedi] ❌ IPC NPC spawn error: {npcClassName}");
                throw new SpawnException($"IPC NPC spawn failed: {ex.Message}", npcClassName, new Vector3(x, 0, z));
            }
        }

        /// <summary>
        /// Уничтожает сущность по EntityId.
        /// На Dedi — запрещено (нет _directSpawner), на PfServer — делегируем к _directSpawner.
        /// </summary>
        public async Task DestroyEntityAsync(int entityId)
        {
            if (_directSpawner == null)
                throw new InvalidOperationException("DestroyEntityAsync is not supported on Dedi — route through IPC");

            await _directSpawner.DestroyEntityAsync(entityId);
        }

        /// <summary>
        /// Уничтожает несколько сущностей пакетом.
        /// На Dedi — запрещено, на PfServer — делегируем к _directSpawner.
        /// </summary>
        public async Task<int> DestroyEntitiesAsync(IEnumerable<int> entityIds)
        {
            if (_directSpawner == null)
                throw new InvalidOperationException("DestroyEntitiesAsync is not supported on Dedi — route through IPC");

            return await _directSpawner.DestroyEntitiesAsync(entityIds);
        }

        /// <summary>
        /// Проверяет существование сущности по EntityId.
        /// На Dedi — запрещено, на PfServer — делегируем к _directSpawner.
        /// </summary>
        public async Task<bool> EntityExistsAsync(int entityId)
        {
            if (_directSpawner == null)
                throw new InvalidOperationException("EntityExistsAsync is not supported on Dedi — route through IPC");

            return await _directSpawner.EntityExistsAsync(entityId);
        }

        /// <summary>
        /// Определяет тип сущности из имени префаба.
        /// Копия метода из EntitySpawner для использования в IPC запросе.
        /// </summary>
        private EntityType GetEntityTypeFromPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return EntityType.Unknown;

            var lower = prefabName.ToLowerInvariant();

            // Проверяем префиксы/суффиксы типов структур
            if (lower.StartsWith("ba-") || lower.Contains("-ba-") || lower.EndsWith("-ba"))
                return EntityType.BA;
            if (lower.StartsWith("cv-") || lower.Contains("-cv-") || lower.EndsWith("-cv"))
                return EntityType.CV;
            if (lower.StartsWith("sv-") || lower.Contains("-sv-") || lower.EndsWith("-sv"))
                return EntityType.SV;
            if (lower.StartsWith("hv-") || lower.Contains("-hv-") || lower.EndsWith("-hv"))
                return EntityType.HV;

            // Проверяем ключевые слова
            if (lower.Contains("base"))
                return EntityType.BA;
            if (lower.Contains("capital") || lower.Contains("carrier"))
                return EntityType.CV;
            if (lower.Contains("small") && lower.Contains("vessel"))
                return EntityType.SV;
            if (lower.Contains("hover"))
                return EntityType.HV;

            // По умолчанию считаем базой (BA)
            return EntityType.BA;
        }
    }
}
