using System;

namespace GalacticExpansion.Core.Simulation.Events
{
    /// <summary>
    /// Событие уничтожения структуры.
    /// Публикуется StructureTracker при обнаружении исчезновения структуры.
    /// </summary>
    public class StructureDestroyedEvent
    {
        /// <summary>
        /// ID структуры (EntityId).
        /// </summary>
        public int EntityId { get; set; }

        /// <summary>
        /// Название playfield, где была структура.
        /// </summary>
        public string Playfield { get; set; }

        /// <summary>
        /// ID фракции-владельца структуры.
        /// </summary>
        public int FactionId { get; set; }

        /// <summary>
        /// Тип структуры (BA, CV, SV, HV).
        /// </summary>
        public string StructureType { get; set; }

        /// <summary>
        /// Время события (UTC).
        /// </summary>
        public DateTime EventTime { get; set; }

        /// <summary>
        /// Создает событие уничтожения структуры.
        /// </summary>
        public StructureDestroyedEvent()
        {
            Playfield = string.Empty;
            StructureType = string.Empty;
            EventTime = DateTime.UtcNow;
        }
    }
}
