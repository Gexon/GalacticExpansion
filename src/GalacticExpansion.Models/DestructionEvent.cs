using System;
using Newtonsoft.Json;

namespace GalacticExpansion.Models
{
    /// <summary>
    /// Представляет событие разрушения структуры колонии.
    /// Используется для отслеживания враждебных действий игроков
    /// и адаптации реакции колонии.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class DestructionEvent
    {
        /// <summary>
        /// Время разрушения
        /// </summary>
        [JsonProperty("Timestamp")]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Entity ID разрушенной структуры
        /// </summary>
        [JsonProperty("DestroyedEntityId")]
        public int DestroyedEntityId { get; set; }

        /// <summary>
        /// Тип разрушенной структуры (Base, ResourceNode, Vessel)
        /// </summary>
        [JsonProperty("DestroyedType")]
        public string DestroyedType { get; set; } = string.Empty;

        /// <summary>
        /// ID того, кто разрушил (Player ID или Entity ID)
        /// </summary>
        [JsonProperty("DestroyedBy")]
        public int? DestroyedBy { get; set; }

        /// <summary>
        /// Конструктор по умолчанию
        /// </summary>
        public DestructionEvent()
        {
            Timestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// Конструктор с параметрами
        /// </summary>
        public DestructionEvent(int entityId, string type, int? destroyedBy = null)
        {
            Timestamp = DateTime.UtcNow;
            DestroyedEntityId = entityId;
            DestroyedType = type;
            DestroyedBy = destroyedBy;
        }
    }
}
