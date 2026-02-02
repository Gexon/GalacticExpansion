using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Economy
{
    /// <summary>
    /// Интерфейс для управления производством, доступностью и учётом боевых юнитов колонии.
    /// Ключевая механика: конечность юнитов - истощение резервов приводит к реальному ослаблению.
    /// </summary>
    public interface IUnitEconomyManager
    {
        /// <summary>
        /// Обновляет производство юнитов для колонии с течением времени
        /// </summary>
        void ProduceUnits(Colony colony, float deltaTime);

        /// <summary>
        /// Проверяет, доступно ли указанное количество юнитов для спавна
        /// </summary>
        bool CanSpawnUnit(Colony colony, UnitType unitType, int count = 1);

        /// <summary>
        /// Резервирует юниты перед спавном (уменьшает AvailableGuards и т.д.)
        /// </summary>
        bool ReserveUnits(Colony colony, UnitType unitType, int count);

        /// <summary>
        /// Регистрирует активный юнит после спавна
        /// </summary>
        void RegisterActiveUnit(Colony colony, int entityId, UnitType unitType, string assignedRole);

        /// <summary>
        /// Учитывает потерю юнита (при уничтожении)
        /// </summary>
        void RecordUnitLoss(Colony colony, int entityId);

        /// <summary>
        /// Пересчитывает вместимость (MaxGuards и т.д.) при смене стадии
        /// </summary>
        void RecalculateCapacity(Colony colony);

        /// <summary>
        /// Обрабатывает разрушение аванпоста (снижает ProductionRate на 25%)
        /// </summary>
        void OnResourceOutpostDestroyed(Colony colony);

        /// <summary>
        /// Обрабатывает разрушение верфи (снижает ProductionRate на 50%)
        /// </summary>
        void OnShipyardDestroyed(Colony colony);
    }

    /// <summary>
    /// Типы боевых юнитов
    /// </summary>
    public enum UnitType
    {
        Guard,          // Охранники (пехота)
        PatrolVessel,   // Патрульные корабли
        Warship,        // Военные корабли
        Drone           // Дроны
    }
}
