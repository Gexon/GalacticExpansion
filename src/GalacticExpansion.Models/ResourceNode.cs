using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GalacticExpansion.Models
{
    /// <summary>
    /// Тип ресурсного аванпоста
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ResourceNodeType
    {
        /// <summary>Железо</summary>
        Iron,
        
        /// <summary>Медь</summary>
        Copper,
        
        /// <summary>Кремний</summary>
        Silicon,
        
        /// <summary>Прометий</summary>
        Promethium,
        
        /// <summary>Смешанный (несколько типов ресурсов)</summary>
        Mixed
    }

    /// <summary>
    /// Представляет ресурсный аванпост колонии.
    /// Аванпосты добывают ресурсы и увеличивают ProductionRate колонии.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ResourceNode
    {
        /// <summary>
        /// Уникальный идентификатор аванпоста
        /// </summary>
        [JsonProperty("Id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Тип добываемого ресурса
        /// </summary>
        [JsonProperty("Type")]
        public ResourceNodeType Type { get; set; }

        /// <summary>
        /// Позиция аванпоста
        /// </summary>
        [JsonProperty("Position")]
        public Vector3 Position { get; set; } = new Vector3();

        /// <summary>
        /// Entity ID структуры аванпоста в игре
        /// </summary>
        [JsonProperty("StructureId")]
        public int? StructureId { get; set; }

        /// <summary>
        /// Скорость производства ресурсов (единиц в час)
        /// </summary>
        [JsonProperty("ProductionRate")]
        public float ProductionRate { get; set; }

        /// <summary>
        /// Время создания аванпоста
        /// </summary>
        [JsonProperty("CreatedAt")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Конструктор по умолчанию
        /// </summary>
        public ResourceNode()
        {
            CreatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Конструктор с параметрами
        /// </summary>
        public ResourceNode(ResourceNodeType type, Vector3 position, float productionRate)
        {
            Type = type;
            Position = position;
            ProductionRate = productionRate;
            CreatedAt = DateTime.UtcNow;
        }
    }
}
