using System;
using System.Linq;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Economy
{
    /// <summary>
    /// Реализация менеджера юнит-экономики колоний.
    /// Управляет производством, доступностью и учётом боевых юнитов.
    /// </summary>
    public class UnitEconomyManager : IUnitEconomyManager
    {
        private readonly Configuration _config;
        private readonly ILogger _logger;

        public UnitEconomyManager(Configuration config, ILogger logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void ProduceUnits(Colony colony, float deltaTime)
        {
            var progress = colony.UnitPool.ProductionRate * (deltaTime / 3600f); // конвертация в часы
            colony.UnitPool.ProductionProgress += progress;

            if (colony.UnitPool.ProductionProgress >= 1.0f)
            {
                var produced = (int)colony.UnitPool.ProductionProgress;
                colony.UnitPool.ProductionProgress -= produced;

                colony.UnitPool.AvailableGuards = Math.Min(
                    colony.UnitPool.AvailableGuards + produced,
                    colony.UnitPool.MaxGuards
                );

                _logger.Info($"Colony {colony.Id}: Produced {produced} guards. Available: {colony.UnitPool.AvailableGuards}/{colony.UnitPool.MaxGuards}");
            }
        }

        public bool CanSpawnUnit(Colony colony, UnitType unitType, int count = 1)
        {
            return unitType switch
            {
                UnitType.Guard => colony.UnitPool.AvailableGuards >= count,
                UnitType.PatrolVessel => colony.UnitPool.AvailablePatrolVessels >= count,
                UnitType.Warship => colony.UnitPool.AvailableWarships >= count,
                UnitType.Drone => colony.UnitPool.AvailableDrones >= count,
                _ => false
            };
        }

        public bool ReserveUnits(Colony colony, UnitType unitType, int count)
        {
            if (!CanSpawnUnit(colony, unitType, count))
                return false;

            switch (unitType)
            {
                case UnitType.Guard:
                    colony.UnitPool.AvailableGuards -= count;
                    break;
                case UnitType.PatrolVessel:
                    colony.UnitPool.AvailablePatrolVessels -= count;
                    break;
                case UnitType.Warship:
                    colony.UnitPool.AvailableWarships -= count;
                    break;
                case UnitType.Drone:
                    colony.UnitPool.AvailableDrones -= count;
                    break;
            }

            _logger.Debug($"Colony {colony.Id}: Reserved {count}x {unitType}");
            return true;
        }

        public void RegisterActiveUnit(Colony colony, int entityId, UnitType unitType, string assignedRole)
        {
            var activeUnit = new ActiveUnit
            {
                EntityId = entityId,
                Type = unitType.ToString(),
                SpawnedAt = DateTime.UtcNow,
                AssignedRole = assignedRole
            };

            colony.UnitPool.ActiveUnits.Add(activeUnit);
            _logger.Trace($"Colony {colony.Id}: Registered active unit {entityId} ({unitType})");
        }

        public void RecordUnitLoss(Colony colony, int entityId)
        {
            var unit = colony.UnitPool.ActiveUnits.FirstOrDefault(u => u.EntityId == entityId);
            if (unit != null)
            {
                colony.UnitPool.ActiveUnits.Remove(unit);
                _logger.Info($"Colony {colony.Id}: Lost unit {entityId} ({unit.Type})");
            }
        }

        public void RecalculateCapacity(Colony colony)
        {
            var stageConfig = _config.Zirax.Stages.FirstOrDefault(s => s.Stage == colony.Stage.ToString());
            if (stageConfig == null)
                return;

            colony.UnitPool.MaxGuards = stageConfig.GuardCount;
            _logger.Info($"Colony {colony.Id}: Capacity recalculated for stage {colony.Stage}. MaxGuards={colony.UnitPool.MaxGuards}");
        }

        public void OnResourceOutpostDestroyed(Colony colony)
        {
            colony.UnitPool.ProductionRate *= 0.75f; // -25%
            _logger.Warn($"Colony {colony.Id}: Resource outpost destroyed. ProductionRate reduced to {colony.UnitPool.ProductionRate:F2}/hour");
        }

        public void OnShipyardDestroyed(Colony colony)
        {
            colony.UnitPool.ProductionRate *= 0.5f; // -50%
            _logger.Warn($"Colony {colony.Id}: Shipyard destroyed. ProductionRate reduced to {colony.UnitPool.ProductionRate:F2}/hour");
        }
    }
}
