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
        /// Помечает виртуальные колонии на playfield для материализации.
        /// Вызывается при Event_Playfield_Loaded и/или IPC PlayfieldReadyNotification.
        /// Фактическая материализация происходит через TryMaterializePendingColoniesAsync (retry-логика).
        /// </summary>
        /// <param name="playfield">Имя загруженного playfield.</param>
        Task EnsurePlayfieldColoniesSpawnedAsync(string playfield);

        /// <summary>
        /// Пытается материализовать колонии, помеченные для материализации (retry-логика).
        /// Вызывается из ModMain через Task.Run() с throttle (не чаще раза в 3 секунды).
        /// НЕЛЬЗЯ вызывать через .GetAwaiter().GetResult() из Game_Update — это вызывает deadlock
        /// на Unity main thread (continuation от await захватывает SynchronizationContext).
        /// При ошибке повторяет попытки до 30 раз.
        /// </summary>
        Task TryMaterializePendingColoniesAsync();

        /// <summary>
        /// Устанавливает ссылку на in-memory SimulationState из SimulationEngine.
        /// Вызывается после старта SimulationEngine, чтобы ColonyManager работал с тем же state,
        /// что и тиковый цикл (без обращения к файлу state.json).
        /// </summary>
        /// <param name="state">In-memory SimulationState из SimulationEngine.</param>
        void SetSimulationState(SimulationState state);
    }
}
