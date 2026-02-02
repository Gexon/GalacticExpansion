using System.Threading.Tasks;
using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Spawning
{
    /// <summary>
    /// Интерфейс для управления жизненным циклом колоний.
    /// Координирует переходы между стадиями, откаты при разрушениях и защиту от авто-удаления структур.
    /// </summary>
    public interface IStageManager
    {
        /// <summary>
        /// Проверяет, готова ли колония к переходу на следующую стадию
        /// </summary>
        Task<bool> CanTransitionToNextStageAsync(Colony colony);

        /// <summary>
        /// Выполняет переход колонии на следующую стадию (полный цикл: удаление, спавн, обновление)
        /// </summary>
        Task TransitionToNextStageAsync(Colony colony);

        /// <summary>
        /// Откатывает колонию на предыдущую стадию при разрушении главной базы
        /// </summary>
        Task DowngradeColonyAsync(Colony colony);

        /// <summary>
        /// Защищает структуры колонии от авто-удаления (Request_Structure_Touch)
        /// Вызывается каждый час
        /// </summary>
        Task MaintainColonyStructuresAsync(Colony colony);

        /// <summary>
        /// Инициализирует новую колонию с DropShip
        /// </summary>
        Task<Colony> InitializeColonyAsync(string playfield, Vector3 position, int factionId);
    }
}
