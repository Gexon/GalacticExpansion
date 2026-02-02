using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Gateway;
using GalacticExpansion.Core.Tracking;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Placement
{
    /// <summary>
    /// Реализация интерфейса IPlacementResolver для поиска подходящих мест размещения структур.
    /// Использует спиральный алгоритм поиска с адаптивным шагом.
    /// 
    /// ПРИМЕЧАНИЕ: Интеграция с IPlayfield.GetTerrainHeightAt() требует расширенного API (IModApi через IMod.Init).
    /// При базовом API (только ModGameAPI через ModInterface.Game_Start) используется fallback с фиксированной высотой,
    /// т.к. ModGameAPI не предоставляет доступ к IApplication и IPlayfield.
    /// Оба режима работают на Dedicated Server, разница только в доступном API.
    /// </summary>
    public class PlacementResolver : IPlacementResolver
    {
        private readonly IEmpyrionGateway _gateway;
        private readonly IPlayerTracker _playerTracker;
        private readonly ILogger _logger;
        private readonly Dictionary<string, IPlayfieldWrapper> _playfieldCache;
        private const float DefaultStepSize = 50f;
        private const float DefaultTerrainHeight = 100f; // Высота по умолчанию для fallback

        /// <summary>
        /// Конструктор PlacementResolver
        /// </summary>
        /// <param name="gateway">Шлюз для взаимодействия с Empyrion API</param>
        /// <param name="playerTracker">Трекер игроков для проверки дистанций</param>
        /// <param name="logger">Логгер</param>
        /// <param name="modApi">Опциональный IModApi (расширенный API) для доступа к IPlayfield через события</param>
        public PlacementResolver(
            IEmpyrionGateway gateway,
            IPlayerTracker playerTracker,
            ILogger logger,
            IModApi? modApi = null)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _playerTracker = playerTracker ?? throw new ArgumentNullException(nameof(playerTracker));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _playfieldCache = new Dictionary<string, IPlayfieldWrapper>();

            // Если доступен IModApi (расширенный API через IMod.Init), подписываемся на события playfield'ов
            if (modApi != null && modApi.Application != null)
            {
                _logger.Info("IModApi available - enabling terrain height detection via IPlayfield");
                
                modApi.Application.OnPlayfieldLoaded += (playfield) =>
                {
                    if (playfield != null)
                    {
                        _playfieldCache[playfield.Name] = new PlayfieldWrapper(playfield);
                        _logger.Debug($"Cached playfield: {playfield.Name}");
                    }
                };

                modApi.Application.OnPlayfieldUnloading += (playfield) =>
                {
                    if (playfield != null && _playfieldCache.ContainsKey(playfield.Name))
                    {
                        _playfieldCache.Remove(playfield.Name);
                        _logger.Debug($"Removed playfield from cache: {playfield.Name}");
                    }
                };
            }
            else
            {
                _logger.Warn("IModApi not available (base ModGameAPI only) - terrain height detection unavailable, using fallback");
                _logger.Warn("For precise terrain height, ensure IMod.Init is called with IModApi");
            }
        }

        public async Task<Vector3> FindSuitableLocationAsync(PlacementCriteria criteria)
        {
            if (criteria == null)
                throw new ArgumentNullException(nameof(criteria));
            if (string.IsNullOrEmpty(criteria.Playfield))
                throw new ArgumentException("Playfield name cannot be empty", nameof(criteria));

            _logger.Debug($"Starting placement search on '{criteria.Playfield}', radius={criteria.SearchRadius}m");

            var stopwatch = Stopwatch.StartNew();
            var center = criteria.PreferredLocation ?? new Vector3();
            var searchRadius = criteria.SearchRadius;
            var stepSize = DefaultStepSize;

            try
            {
                var allStructures = await _gateway.SendRequestAsync<Dictionary<string, List<GlobalStructureInfo>>>(
                    CmdId.Request_GlobalStructure_List, null, timeoutMs: 5000);

                var structures = allStructures != null && allStructures.ContainsKey(criteria.Playfield)
                    ? allStructures[criteria.Playfield] : new List<GlobalStructureInfo>();

                _logger.Trace($"Found {structures.Count} existing structures on '{criteria.Playfield}'");

                int checkedPositions = 0;
                for (float radius = 0; radius < searchRadius; radius += stepSize)
                {
                    if (stopwatch.Elapsed.TotalSeconds >= criteria.MaxSearchTimeSeconds)
                    {
                        _logger.Warn($"Placement search timeout ({criteria.MaxSearchTimeSeconds}s)");
                        throw new PlacementException($"Search timeout after {criteria.MaxSearchTimeSeconds} seconds",
                            criteria.Playfield, searchRadius);
                    }

                    var angleStep = stepSize / Math.Max(radius, 1f);
                    var radiansStep = (float)(angleStep * Math.PI / 180.0);

                    for (float angle = 0; angle < 2 * Math.PI; angle += radiansStep)
                    {
                        checkedPositions++;
                        var testX = center.X + radius * (float)Math.Cos(angle);
                        var testZ = center.Z + radius * (float)Math.Sin(angle);

                        Vector3 testPosition;
                        if (criteria.UseTerrainHeight)
                        {
                            try
                            {
                                testPosition = await FindLocationAtTerrainAsync(criteria.Playfield, testX, testZ, criteria.HeightOffset);
                            }
                            catch (Exception ex)
                            {
                                _logger.Trace($"Failed to get terrain height at ({testX}, {testZ}): {ex.Message}");
                                continue;
                            }
                        }
                        else
                        {
                            testPosition = new Vector3(testX, center.Y, testZ);
                        }

                        if (await IsLocationSuitableInternalAsync(testPosition, structures, criteria))
                        {
                            stopwatch.Stop();
                            _logger.Info($"✅ Found suitable location at {testPosition} (checked {checkedPositions} positions in {stopwatch.ElapsedMilliseconds}ms)");
                            return testPosition;
                        }
                    }
                }

                stopwatch.Stop();
                _logger.Error($"❌ No suitable location found on '{criteria.Playfield}' within radius {searchRadius}m");
                throw new PlacementException($"No suitable location found within {searchRadius}m radius",
                    criteria.Playfield, searchRadius);
            }
            catch (PlacementException) { throw; }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error during placement search on '{criteria.Playfield}'");
                throw new PlacementException($"Placement search failed: {ex.Message}", ex);
            }
        }

        public async Task<bool> IsLocationSuitableAsync(Vector3 position, PlacementCriteria criteria)
        {
            if (criteria == null)
                throw new ArgumentNullException(nameof(criteria));

            try
            {
                var allStructures = await _gateway.SendRequestAsync<Dictionary<string, List<GlobalStructureInfo>>>(
                    CmdId.Request_GlobalStructure_List, null, timeoutMs: 5000);

                var structures = allStructures != null && allStructures.ContainsKey(criteria.Playfield)
                    ? allStructures[criteria.Playfield] : new List<GlobalStructureInfo>();

                return await IsLocationSuitableInternalAsync(position, structures, criteria);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error checking location suitability at {position}");
                return false;
            }
        }

        private async Task<bool> IsLocationSuitableInternalAsync(
            Vector3 position, List<GlobalStructureInfo> structures, PlacementCriteria criteria)
        {
            foreach (var structure in structures)
            {
                if (structure.factionId == criteria.FactionId)
                    continue;

                var structurePos = new Vector3(structure.pos.x, structure.pos.y, structure.pos.z);
                var distance = position.DistanceTo(structurePos);

                if (distance < criteria.MinDistanceFromPlayerStructures)
                {
                    _logger.Trace($"Position {position} too close to player structure (distance={distance:F1}m)");
                    return false;
                }
            }

            if (criteria.MinDistanceFromPlayers > 0)
            {
                var players = _playerTracker.GetPlayersOnPlayfield(criteria.Playfield);
                foreach (var player in players)
                {
                    var distance = position.DistanceTo(player.Position);
                    if (distance < criteria.MinDistanceFromPlayers)
                    {
                        _logger.Trace($"Position {position} too close to player (distance={distance:F1}m)");
                        return false;
                    }
                }
            }

            return true;
        }

        public float GetTerrainHeight(IPlayfieldWrapper playfieldWrapper, float x, float z)
        {
            if (playfieldWrapper == null)
                throw new ArgumentNullException(nameof(playfieldWrapper));

            try
            {
                return playfieldWrapper.GetTerrainHeight(x, z);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get terrain height at ({x}, {z})");
                throw new PlacementException($"Cannot determine terrain height: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Находит позицию на рельефе с точной высотой (если доступен IPlayfield) или fallback.
        /// 
        /// С IModApi (расширенный API): Использует IPlayfield.GetTerrainHeightAt() для точной высоты рельефа.
        /// Без IModApi (базовый API): Использует фиксированную высоту 100м (ModGameAPI не предоставляет IPlayfield).
        /// 
        /// Оба режима работают на Dedicated Server. Это решает проблему спавна структур под землей или в воздухе.
        /// </summary>
        /// <param name="playfieldName">Название playfield</param>
        /// <param name="x">Координата X</param>
        /// <param name="z">Координата Z</param>
        /// <param name="heightOffset">Отступ над поверхностью земли (метры), по умолчанию 0.5м</param>
        /// <returns>Vector3 с координатой Y</returns>
        public async Task<Vector3> FindLocationAtTerrainAsync(
            string playfieldName, float x, float z, float heightOffset = 0.5f)
        {
            if (string.IsNullOrEmpty(playfieldName))
                throw new ArgumentException("Playfield name cannot be empty", nameof(playfieldName));

            float terrainHeight = DefaultTerrainHeight;

            // Пытаемся получить точную высоту если playfield закэширован (требует IModApi)
            if (_playfieldCache.TryGetValue(playfieldName, out var playfield))
            {
                try
                {
                    terrainHeight = GetTerrainHeight(playfield, x, z);
                    _logger.Trace($"Got precise terrain height {terrainHeight:F1}m for {playfieldName} at ({x:F1}, {z:F1})");
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Failed to get terrain height for {playfieldName}, using fallback {DefaultTerrainHeight}m");
                    terrainHeight = DefaultTerrainHeight;
                }
            }
            else
            {
                _logger.Trace($"Playfield '{playfieldName}' not in cache, using fallback height {DefaultTerrainHeight}m");
            }
            
            var position = new Vector3(x, terrainHeight + heightOffset, z);
            _logger.Trace($"Terrain position at ({x:F1}, {z:F1}) = Y {position.Y:F1}m (terrain={terrainHeight:F1}m + offset={heightOffset:F1}m)");

            return await Task.FromResult(position);
        }
    }
}
