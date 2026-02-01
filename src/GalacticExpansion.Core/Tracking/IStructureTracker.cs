using System.Collections.Generic;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Simulation;

namespace GalacticExpansion.Core.Tracking
{
    /// <summary>
    /// Интерфейс для отслеживания структур на сервере.
    /// Периодически обновляет список структур и детектирует создание/уничтожение.
    /// </summary>
    public interface IStructureTracker : ISimulationModule
    {
        /// <summary>
        /// Принудительно обновляет список структур.
        /// Обычно вызывается автоматически каждые 10 секунд.
        /// </summary>
        Task RefreshStructuresAsync();

        /// <summary>
        /// Получает список структур на указанном playfield.
        /// </summary>
        /// <param name="playfield">Название playfield</param>
        /// <param name="factionId">Опциональный фильтр по фракции</param>
        /// <returns>Список структур</returns>
        List<GlobalStructureInfo> GetStructuresOnPlayfield(string playfield, int? factionId = null);

        /// <summary>
        /// Получает информацию о структуре по ID.
        /// </summary>
        /// <param name="entityId">ID структуры</param>
        /// <returns>Информация о структуре или null если не найдена</returns>
        GlobalStructureInfo? GetStructure(int entityId);

        /// <summary>
        /// Проверяет, существует ли структура.
        /// </summary>
        /// <param name="entityId">ID структуры</param>
        /// <returns>True если структура существует</returns>
        bool StructureExists(int entityId);
    }
}
