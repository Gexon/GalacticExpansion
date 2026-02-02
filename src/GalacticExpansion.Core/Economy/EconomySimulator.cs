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

        public EconomySimulator(Configuration config, ILogger logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void UpdateProduction(Colony colony, float deltaTime)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));

            _logger.Trace($"Colony {colony.Id}: Production for deltaTime={deltaTime}s");

            float productionRate = colony.Resources.ProductionRate;
            float bonus = colony.Resources.ProductionBonus;
            float modifier = 1 + (bonus / 100f);
            float produced = productionRate * modifier * deltaTime;

            colony.Resources.VirtualResources += produced;
        }

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

        public void ConsumeResourcesForUpgrade(Colony colony, float cost)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));

            if (colony.Resources.VirtualResources < cost)
                throw new InvalidOperationException($"Not enough resources. Have: {colony.Resources.VirtualResources:F2}, Need: {cost:F2}");

            colony.Resources.VirtualResources -= cost;
            _logger.Info($"Colony {colony.Id}: Consumed {cost} resources for upgrade. Remaining: {colony.Resources.VirtualResources:F2}");
        }

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
