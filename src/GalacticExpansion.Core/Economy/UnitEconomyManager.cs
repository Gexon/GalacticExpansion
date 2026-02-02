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

        /// <summary>
        /// Создаёт менеджер юнит-экономики с заданной конфигурацией и логгером.
        /// </summary>
        /// <param name="config">Конфигурация мода.</param>
        /// <param name="logger">Логгер.</param>
        public UnitEconomyManager(Configuration config, ILogger logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Обновляет производство юнитов для колонии за интервал времени.
        /// </summary>
        /// <param name="colony">Колония.</param>
        /// <param name="deltaTime">Интервал времени в секундах.</param>
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

        /// <summary>
        /// Проверяет, доступно ли указанное количество юнитов данного типа для спавна.
        /// </summary>
        /// <param name="colony">Колония.</param>
        /// <param name="unitType">Тип юнита.</param>
        /// <param name="count">Количество (по умолчанию 1).</param>
        /// <returns>True, если юниты доступны.</returns>
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

        /// <summary>
        /// Резервирует юниты перед спавном (уменьшает AvailableGuards и т.д.).
        /// </summary>
        /// <param name="colony">Колония.</param>
        /// <param name="unitType">Тип юнита.</param>
        /// <param name="count">Количество.</param>
        /// <returns>True, если резерв успешен.</returns>
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

        /// <summary>
        /// Регистрирует активный юнит после спавна в игре.
        /// </summary>
        /// <param name="colony">Колония.</param>
        /// <param name="entityId">Идентификатор сущности в игре.</param>
        /// <param name="unitType">Тип юнита.</param>
        /// <param name="assignedRole">Назначенная роль (например BaseDefense).</param>
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

        /// <summary>
        /// Учитывает потерю юнита (при уничтожении в игре).
        /// </summary>
        /// <param name="colony">Колония.</param>
        /// <param name="entityId">Идентификатор уничтоженной сущности.</param>
        public void RecordUnitLoss(Colony colony, int entityId)
        {
            var unit = colony.UnitPool.ActiveUnits.FirstOrDefault(u => u.EntityId == entityId);
            if (unit != null)
            {
                colony.UnitPool.ActiveUnits.Remove(unit);
                _logger.Info($"Colony {colony.Id}: Lost unit {entityId} ({unit.Type})");
            }
        }

        /// <summary>
        /// Пересчитывает вместимость (MaxGuards и т.д.) при смене стадии колонии.
        /// </summary>
        /// <param name="colony">Колония.</param>
        public void RecalculateCapacity(Colony colony)
        {
            var stageConfig = _config.Zirax.Stages.FirstOrDefault(s => s.Stage == colony.Stage.ToString());
            if (stageConfig == null)
                return;

            colony.UnitPool.MaxGuards = stageConfig.GuardCount;
            _logger.Info($"Colony {colony.Id}: Capacity recalculated for stage {colony.Stage}. MaxGuards={colony.UnitPool.MaxGuards}");
        }

        /// <summary>
        /// Обрабатывает разрушение аванпоста: снижает ProductionRate на 25%.
        /// </summary>
        /// <param name="colony">Колония.</param>
        public void OnResourceOutpostDestroyed(Colony colony)
        {
            colony.UnitPool.ProductionRate *= 0.75f; // -25%
            _logger.Warn($"Colony {colony.Id}: Resource outpost destroyed. ProductionRate reduced to {colony.UnitPool.ProductionRate:F2}/hour");
        }

        /// <summary>
        /// Обрабатывает разрушение верфи: снижает ProductionRate на 50%.
        /// </summary>
        /// <param name="colony">Колония.</param>
        public void OnShipyardDestroyed(Colony colony)
        {
            colony.UnitPool.ProductionRate *= 0.5f; // -50%
            _logger.Warn($"Colony {colony.Id}: Shipyard destroyed. ProductionRate reduced to {colony.UnitPool.ProductionRate:F2}/hour");
        }
    }
}
