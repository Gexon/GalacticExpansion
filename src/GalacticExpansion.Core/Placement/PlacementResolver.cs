using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// </summary>
    public class PlacementResolver : IPlacementResolver
    {
        private readonly IEmpyrionGateway _gateway;
        private readonly IPlayerTracker _playerTracker;
        private readonly ILogger _logger;
        private const float DefaultStepSize = 50f;

        public PlacementResolver(
            IEmpyrionGateway gateway,
            IPlayerTracker playerTracker,
            ILogger logger)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _playerTracker = playerTracker ?? throw new ArgumentNullException(nameof(playerTracker));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        public float GetTerrainHeight(IPlayfield playfield, float x, float z)
        {
            if (playfield == null)
                throw new ArgumentNullException(nameof(playfield));

            try
            {
                return playfield.GetTerrainHeightAt(x, z);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to get terrain height at ({x}, {z})");
                throw new PlacementException($"Cannot determine terrain height: {ex.Message}", ex);
            }
        }

        public async Task<Vector3> FindLocationAtTerrainAsync(
            string playfieldName, float x, float z, float heightOffset = 0.5f)
        {
            if (string.IsNullOrEmpty(playfieldName))
                throw new ArgumentException("Playfield name cannot be empty", nameof(playfieldName));

            // TODO: Интегрировать с IPlayfield.GetTerrainHeightAt() через ModAPI
            // Временно используем фиксированную высоту
            float terrainHeight = 100f;

            var position = new Vector3(x, terrainHeight + heightOffset, z);
            _logger.Trace($"Terrain position at ({x:F1}, {z:F1}) = Y {position.Y:F1}m");

            return await Task.FromResult(position);
        }
    }
}
