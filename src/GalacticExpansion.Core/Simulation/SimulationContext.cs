using System;
using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Контекст одного тика симуляции.
    /// Передается во все модули при вызове OnSimulationUpdate.
    /// </summary>
    public class SimulationContext
    {
        /// <summary>
        /// Текущее состояние симуляции (read-only для модулей).
        /// Модули могут читать state, но изменения должны делаться через специальные методы.
        /// </summary>
        public SimulationState CurrentState { get; set; }

        /// <summary>
        /// Время, прошедшее с предыдущего тика (в секундах).
        /// Обычно ~1.0 секунда, но может варьироваться при лагах.
        /// </summary>
        public float DeltaTime { get; set; }

        /// <summary>
        /// Текущее время сервера (UTC).
        /// </summary>
        public DateTime CurrentTime { get; set; }

        /// <summary>
        /// Номер текущего тика (инкрементируется с каждым тиком).
        /// Полезно для периодических операций (например, каждые 10 тиков).
        /// </summary>
        public long TickNumber { get; set; }

        /// <summary>
        /// Создает новый контекст симуляции.
        /// </summary>
        public SimulationContext()
        {
            CurrentState = new SimulationState();
            DeltaTime = 1.0f;
            CurrentTime = DateTime.UtcNow;
            TickNumber = 0;
        }
    }
}
