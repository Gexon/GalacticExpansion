using System.Threading.Tasks;
using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Интерфейс для упрощенного управления колониями.
    /// Координирует Economy Simulator, Unit Economy Manager и Stage Manager.
    /// Поддерживает виртуализацию колоний (существование в БД без физического спавна).
    /// </summary>
    public interface IColonyManager
    {
        /// <summary>
        /// Обновляет колонию: экономику, производство юнитов, проверку апгрейдов
        /// </summary>
        Task UpdateColonyAsync(Colony colony, float deltaTime);

        /// <summary>
        /// Создает новую колонию (может быть виртуальной)
        /// </summary>
        /// <param name="playfield">Название playfield</param>
        /// <param name="position">Позиция (может быть нулевой для виртуальных)</param>
        /// <param name="factionId">ID фракции</param>
        /// <param name="isVirtual">Создать виртуальную колонию (без спавна структур)</param>
        Task<Colony> CreateColonyAsync(string playfield, Vector3 position, int factionId, bool isVirtual = false);

        /// <summary>
        /// Удаляет колонию
        /// </summary>
        Task RemoveColonyAsync(string colonyId);

        /// <summary>
        /// При загрузке playfield (Event_Playfield_Loaded): материализация виртуальных колоний и обновление/защита структур.
        /// Виртуальные колонии материализуются (спавнятся структуры), существующие - получают Touch от decay.
        /// </summary>
        /// <param name="playfield">Имя загруженного playfield.</param>
        Task EnsurePlayfieldColoniesSpawnedAsync(string playfield);
    }
}
