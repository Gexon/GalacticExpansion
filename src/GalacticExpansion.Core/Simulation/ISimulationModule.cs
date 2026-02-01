using System.Threading.Tasks;
using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Базовый интерфейс для всех модулей симуляции.
    /// Определяет жизненный цикл модуля: Initialize -> Update (каждый тик) -> Shutdown.
    /// </summary>
    public interface ISimulationModule
    {
        /// <summary>
        /// Уникальное имя модуля для логирования и идентификации.
        /// </summary>
        string ModuleName { get; }

        /// <summary>
        /// Приоритет обновления модуля (меньше = раньше).
        /// Модули с меньшим приоритетом обновляются первыми.
        /// Рекомендуемые значения:
        /// - 10: PlayerTracker, StructureTracker (сбор данных)
        /// - 50: Economy, Evolution (обработка логики)
        /// - 100: Threat Director, AIM (реакция на изменения)
        /// </summary>
        int UpdatePriority { get; }

        /// <summary>
        /// Инициализация модуля при старте симуляции.
        /// Вызывается один раз перед первым тиком.
        /// </summary>
        /// <param name="state">Текущее состояние симуляции</param>
        Task InitializeAsync(SimulationState state);

        /// <summary>
        /// Обновление модуля на каждом тике симуляции.
        /// Должно выполняться быстро (менее 100ms рекомендуется).
        /// </summary>
        /// <param name="context">Контекст текущего тика</param>
        void OnSimulationUpdate(SimulationContext context);

        /// <summary>
        /// Корректное завершение работы модуля.
        /// Вызывается при остановке симуляции для освобождения ресурсов.
        /// </summary>
        Task ShutdownAsync();
    }
}
