using System;

namespace GalacticExpansion.Core.Simulation.Events
{
    /// <summary>
    /// Событие выхода игрока с playfield.
    /// Публикуется PlayerTracker при детектировании смены playfield игроком.
    /// </summary>
    public class PlayerLeftPlayfieldEvent
    {
        /// <summary>
        /// ID игрока.
        /// </summary>
        public int PlayerId { get; set; }

        /// <summary>
        /// Имя игрока.
        /// </summary>
        public string PlayerName { get; set; }

        /// <summary>
        /// Название playfield, с которого ушел игрок.
        /// </summary>
        public string Playfield { get; set; }

        /// <summary>
        /// Время события (UTC).
        /// </summary>
        public DateTime EventTime { get; set; }

        /// <summary>
        /// Создает событие выхода игрока с playfield.
        /// </summary>
        public PlayerLeftPlayfieldEvent()
        {
            PlayerName = string.Empty;
            Playfield = string.Empty;
            EventTime = DateTime.UtcNow;
        }
    }
}
