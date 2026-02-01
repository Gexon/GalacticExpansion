using System.Threading.Tasks;
using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Главный движок симуляции с таймером тиков.
    /// Управляет жизненным циклом симуляции: Start -> Tick Loop -> Stop.
    /// </summary>
    public interface ISimulationEngine
    {
        /// <summary>
        /// Запускает симуляцию.
        /// Загружает state, инициализирует модули, запускает таймер тиков.
        /// </summary>
        Task StartAsync();

        /// <summary>
        /// Останавливает симуляцию корректно.
        /// Останавливает таймер, сохраняет state, завершает модули.
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// Регистрирует модуль в симуляции.
        /// Должно быть вызвано до StartAsync.
        /// </summary>
        /// <param name="module">Модуль для регистрации</param>
        void RegisterModule(ISimulationModule module);

        /// <summary>
        /// Текущее состояние симуляции (read-only).
        /// </summary>
        SimulationState State { get; }

        /// <summary>
        /// Флаг, показывающий, запущена ли симуляция.
        /// </summary>
        bool IsRunning { get; }
    }
}
