using System.Threading.Tasks;
using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Интерфейс для упрощенного управления колониями.
    /// Координирует Economy Simulator, Unit Economy Manager и Stage Manager.
    /// </summary>
    public interface IColonyManager
    {
        /// <summary>
        /// Обновляет колонию: экономику, производство юнитов, проверку апгрейдов
        /// </summary>
        Task UpdateColonyAsync(Colony colony, float deltaTime);

        /// <summary>
        /// Создает новую колонию
        /// </summary>
        Task<Colony> CreateColonyAsync(string playfield, Vector3 position, int factionId);

        /// <summary>
        /// Удаляет колонию
        /// </summary>
        Task RemoveColonyAsync(string colonyId);

        /// <summary>
        /// При загрузке playfield (Event_Playfield_Loaded): обновление/защита структур колоний на этом playfield.
        /// Колонии виртуальны; при загрузке playfield вызывается Touch структур (защита от decay) и в будущем — спавн недостающих структур/юнитов.
        /// </summary>
        /// <param name="playfield">Имя загруженного playfield.</param>
        Task EnsurePlayfieldColoniesSpawnedAsync(string playfield);
    }
}
