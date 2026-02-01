using System;

namespace GalacticExpansion.Core.Simulation.Events
{
    /// <summary>
    /// Событие входа игрока на playfield.
    /// Публикуется PlayerTracker при детектировании смены playfield игроком.
    /// </summary>
    public class PlayerEnteredPlayfieldEvent
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
        /// Название playfield, на который вошел игрок.
        /// </summary>
        public string Playfield { get; set; }

        /// <summary>
        /// Время события (UTC).
        /// </summary>
        public DateTime EventTime { get; set; }

        /// <summary>
        /// Создает событие входа игрока на playfield.
        /// </summary>
        public PlayerEnteredPlayfieldEvent()
        {
            PlayerName = string.Empty;
            Playfield = string.Empty;
            EventTime = DateTime.UtcNow;
        }
    }
}
