using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Economy
{
    /// <summary>
    /// Интерфейс для управления виртуальной экономикой колоний.
    /// Виртуальные ресурсы используются только для расчета прогрессии (не настоящие игровые ресурсы).
    /// </summary>
    public interface IEconomySimulator
    {
        /// <summary>
        /// Обновляет производство ресурсов для колонии.
        /// Формула: resources += ProductionRate × (1 + Bonus/100) × deltaTime
        /// </summary>
        void UpdateProduction(Colony colony, float deltaTime);

        /// <summary>
        /// Добавляет ресурсный аванпост (увеличивает ProductionBonus на 20%)
        /// </summary>
        void AddResourceNode(Colony colony, ResourceNode node);

        /// <summary>
        /// Удаляет ресурсный аванпост (уменьшает ProductionBonus на 20%)
        /// </summary>
        void RemoveResourceNode(Colony colony, string nodeId);

        /// <summary>
        /// Проверяет достаточность ресурсов для апгрейда на следующую стадию
        /// </summary>
        bool HasEnoughResourcesForUpgrade(Colony colony);

        /// <summary>
        /// Списывает ресурсы при апгрейде
        /// </summary>
        void ConsumeResourcesForUpgrade(Colony colony, float resourceCost);

        /// <summary>
        /// Расчет времени до накопления ресурсов для апгрейда
        /// </summary>
        float GetTimeUntilNextUpgradeSeconds(Colony colony, float requiredResources);
    }
}
