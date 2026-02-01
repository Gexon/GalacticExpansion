using System.Collections.Generic;
using GalacticExpansion.Core.Simulation;
using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Tracking
{
    /// <summary>
    /// Интерфейс для отслеживания игроков на playfield'ах.
    /// Предоставляет информацию о местоположении и статусе игроков.
    /// </summary>
    public interface IPlayerTracker : ISimulationModule
    {
        /// <summary>
        /// Получает список игроков на указанном playfield.
        /// </summary>
        /// <param name="playfield">Название playfield</param>
        /// <returns>Список игроков на playfield (пустой список если никого нет)</returns>
        List<TrackedPlayerInfo> GetPlayersOnPlayfield(string playfield);

        /// <summary>
        /// Проверяет, есть ли игроки на указанном playfield.
        /// </summary>
        /// <param name="playfield">Название playfield</param>
        /// <returns>True если есть хотя бы один игрок</returns>
        bool HasPlayersOnPlayfield(string playfield);

        /// <summary>
        /// Получает игроков рядом с указанной позицией.
        /// </summary>
        /// <param name="playfield">Название playfield</param>
        /// <param name="position">Центральная позиция</param>
        /// <param name="radius">Радиус поиска (в метрах)</param>
        /// <returns>Список игроков в радиусе</returns>
        List<TrackedPlayerInfo> GetPlayersNearPosition(string playfield, Vector3 position, float radius);

        /// <summary>
        /// Проверяет, онлайн ли игрок.
        /// </summary>
        /// <param name="playerId">ID игрока</param>
        /// <returns>True если игрок онлайн</returns>
        bool IsPlayerOnline(int playerId);

        /// <summary>
        /// Получает информацию об игроке по ID.
        /// </summary>
        /// <param name="playerId">ID игрока</param>
        /// <returns>Информация об игроке или null если не найден</returns>
        TrackedPlayerInfo? GetPlayer(int playerId);
    }
}
