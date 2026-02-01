using System.Threading.Tasks;
using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Реестр симуляционных модулей с управлением их жизненным циклом.
    /// Обеспечивает правильный порядок инициализации, обновления и завершения модулей.
    /// </summary>
    public interface IModuleRegistry
    {
        /// <summary>
        /// Получает количество зарегистрированных модулей.
        /// </summary>
        int ModuleCount { get; }

        /// <summary>
        /// Регистрирует модуль в реестре.
        /// Модули автоматически сортируются по UpdatePriority.
        /// </summary>
        /// <param name="module">Модуль для регистрации</param>
        void RegisterModule(ISimulationModule module);

        /// <summary>
        /// Инициализирует все зарегистрированные модули.
        /// Вызывается при старте симуляции.
        /// </summary>
        /// <param name="state">Начальное состояние симуляции</param>
        Task InitializeAllModulesAsync(SimulationState state);

        /// <summary>
        /// Обновляет все модули в порядке их приоритета.
        /// Вызывается на каждом тике симуляции.
        /// </summary>
        /// <param name="context">Контекст текущего тика</param>
        void UpdateAllModules(SimulationContext context);

        /// <summary>
        /// Корректно завершает работу всех модулей.
        /// Вызывается при остановке симуляции.
        /// </summary>
        Task ShutdownAllModulesAsync();
    }
}
