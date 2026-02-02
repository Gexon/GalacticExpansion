using System;
using System.Linq;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Economy
{
    /// <summary>
    /// Симулятор экономики колонии.
    /// Управляет виртуальными ресурсами, производством и аванпостами.
    /// </summary>
    public class EconomySimulator : IEconomySimulator
    {
        private readonly Configuration _config;
        private readonly ILogger _logger;
        private const float ResourceNodeBonus = 20f;

        /// <summary>
        /// Создаёт симулятор экономики с заданной конфигурацией и логгером.
        /// </summary>
        /// <param name="config">Конфигурация мода (стадии, ресурсы).</param>
        /// <param name="logger">Логгер для диагностики.</param>
        public EconomySimulator(Configuration config, ILogger logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Обновляет производство виртуальных ресурсов колонии за интервал времени.
        /// </summary>
        /// <param name="colony">Колония.</param>
        /// <param name="deltaTime">Интервал времени в секундах.</param>
        public void UpdateProduction(Colony colony, float deltaTime)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));

            _logger.Debug($"EconomySimulator: colony {colony.Id} production dt={deltaTime:F2}s, rate={colony.Resources.ProductionRate}, resources before={colony.Resources.VirtualResources:F0}");

            float productionRate = colony.Resources.ProductionRate;
            float bonus = colony.Resources.ProductionBonus;
            float modifier = 1 + (bonus / 100f);
            float produced = productionRate * modifier * deltaTime;

            colony.Resources.VirtualResources += produced;
        }

        /// <summary>
        /// Добавляет аванпост ресурсов к колонии и увеличивает ProductionBonus.
        /// </summary>
        /// <param name="colony">Колония.</param>
        /// <param name="node">Узел ресурсов (аванпост).</param>
        public void AddResourceNode(Colony colony, ResourceNode node)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));
            if (node == null)
                throw new ArgumentNullException(nameof(node));

            if (!colony.ResourceNodes.Any(n => n.Id == node.Id))
            {
                colony.ResourceNodes.Add(node);
                colony.Resources.ProductionBonus += ResourceNodeBonus;
                _logger.Info($"Colony {colony.Id}: Added resource node '{node.Id}'. Bonus now {colony.Resources.ProductionBonus:F2}%");
            }
        }

        /// <summary>
        /// Удаляет аванпост ресурсов по идентификатору и уменьшает ProductionBonus.
        /// </summary>
        /// <param name="colony">Колония.</param>
        /// <param name="nodeId">Идентификатор узла.</param>
        public void RemoveResourceNode(Colony colony, string nodeId)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));
            if (string.IsNullOrEmpty(nodeId))
                throw new ArgumentException("Node ID cannot be empty", nameof(nodeId));

            var node = colony.ResourceNodes.FirstOrDefault(n => n.Id == nodeId);
            if (node != null)
            {
                colony.ResourceNodes.Remove(node);
                colony.Resources.ProductionBonus -= ResourceNodeBonus;
                _logger.Info($"Colony {colony.Id}: Removed resource node '{nodeId}'. Bonus now {colony.Resources.ProductionBonus:F2}%");
            }
            else
            {
                _logger.Warn($"Resource node '{nodeId}' not found in colony {colony.Id}");
            }
        }

        /// <summary>
        /// Проверяет, достаточно ли виртуальных ресурсов для перехода на следующую стадию.
        /// </summary>
        /// <param name="colony">Колония.</param>
        /// <returns>True, если ресурсов достаточно.</returns>
        public bool HasEnoughResourcesForUpgrade(Colony colony)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));

            var nextStage = colony.Stage.GetNextStage();
            var stageConfig = _config.Zirax.Stages.FirstOrDefault(s => s.Stage == nextStage.ToString());
            if (stageConfig == null)
                return false;

            return colony.Resources.VirtualResources >= stageConfig.RequiredResources;
        }

        /// <summary>
        /// Списывает виртуальные ресурсы при апгрейде колонии.
        /// </summary>
        /// <param name="colony">Колония.</param>
        /// <param name="cost">Стоимость в виртуальных ресурсах.</param>
        public void ConsumeResourcesForUpgrade(Colony colony, float cost)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));

            if (colony.Resources.VirtualResources < cost)
                throw new InvalidOperationException($"Not enough resources. Have: {colony.Resources.VirtualResources:F2}, Need: {cost:F2}");

            colony.Resources.VirtualResources -= cost;
            _logger.Info($"Colony {colony.Id}: Consumed {cost} resources for upgrade. Remaining: {colony.Resources.VirtualResources:F2}");
        }

        /// <summary>
        /// Вычисляет время в секундах до накопления требуемых ресурсов для апгрейда.
        /// </summary>
        /// <param name="colony">Колония.</param>
        /// <param name="requiredResources">Требуемое количество ресурсов.</param>
        /// <returns>Секунды до апгрейда; 0 если уже достаточно; MaxValue если производство нулевое.</returns>
        public float GetTimeUntilNextUpgradeSeconds(Colony colony, float requiredResources)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));

            float current = colony.Resources.VirtualResources;

            if (current >= requiredResources)
                return 0f;

            float remaining = requiredResources - current;
            float productionRate = colony.Resources.ProductionRate;
            float bonus = colony.Resources.ProductionBonus;
            float effectiveRate = productionRate * (1 + bonus / 100f);

            if (effectiveRate <= 0)
                return float.MaxValue;

            return remaining / effectiveRate;
        }
    }
}
