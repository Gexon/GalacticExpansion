using System;

namespace GalacticExpansion.Core.Simulation.Events
{
    /// <summary>
    /// Событие, публикуемое при старте симуляции.
    /// Модули могут подписаться на это событие для выполнения дополнительной инициализации.
    /// </summary>
    public class SimulationStartedEvent
    {
        /// <summary>
        /// Время старта симуляции (UTC).
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Количество зарегистрированных модулей.
        /// </summary>
        public int ModuleCount { get; set; }

        /// <summary>
        /// Создает событие старта симуляции.
        /// </summary>
        public SimulationStartedEvent()
        {
            StartTime = DateTime.UtcNow;
            ModuleCount = 0;
        }
    }
}
