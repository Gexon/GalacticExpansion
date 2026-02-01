using System;

namespace GalacticExpansion.Core.Simulation.Events
{
    /// <summary>
    /// Событие, публикуемое на каждом тике симуляции.
    /// Полезно для модулей, которым нужно реагировать после обновления всех остальных модулей.
    /// </summary>
    public class SimulationTickEvent
    {
        /// <summary>
        /// Номер текущего тика.
        /// </summary>
        public long TickNumber { get; set; }

        /// <summary>
        /// Время выполнения тика (в миллисекундах).
        /// </summary>
        public long TickDurationMs { get; set; }

        /// <summary>
        /// Время тика (UTC).
        /// </summary>
        public DateTime TickTime { get; set; }

        /// <summary>
        /// Создает событие тика симуляции.
        /// </summary>
        public SimulationTickEvent()
        {
            TickNumber = 0;
            TickDurationMs = 0;
            TickTime = DateTime.UtcNow;
        }
    }
}
