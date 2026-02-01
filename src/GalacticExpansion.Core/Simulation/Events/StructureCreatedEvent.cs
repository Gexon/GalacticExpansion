using System;

namespace GalacticExpansion.Core.Simulation.Events
{
    /// <summary>
    /// Событие создания структуры.
    /// Публикуется StructureTracker при обнаружении новой структуры.
    /// </summary>
    public class StructureCreatedEvent
    {
        /// <summary>
        /// ID структуры (EntityId).
        /// </summary>
        public int EntityId { get; set; }

        /// <summary>
        /// Название playfield, где создана структура.
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
        /// Создает событие создания структуры.
        /// </summary>
        public StructureCreatedEvent()
        {
            Playfield = string.Empty;
            StructureType = string.Empty;
            EventTime = DateTime.UtcNow;
        }
    }
}
