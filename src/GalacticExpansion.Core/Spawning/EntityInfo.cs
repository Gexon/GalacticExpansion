using System;
using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Spawning
{
    /// <summary>
    /// Информация о заспавненной игровой сущности.
    /// Содержит данные о типе, позиции, фракции и времени создания.
    /// Используется для отслеживания и управления сущностями.
    /// </summary>
    public class EntityInfo
    {
        /// <summary>
        /// ID сущности в игре
        /// </summary>
        public int EntityId { get; set; }

        /// <summary>
        /// Название сущности (или префаб)
        /// </summary>
        public string EntityName { get; set; } = string.Empty;

        /// <summary>
        /// Тип сущности (BA, CV, SV, HV, NPC, Player)
        /// </summary>
        public EntityType EntityType { get; set; }

        /// <summary>
        /// Позиция сущности в мире
        /// </summary>
        public Vector3 Position { get; set; } = new Vector3();

        /// <summary>
        /// ID фракции сущности
        /// </summary>
        public int FactionId { get; set; }

        /// <summary>
        /// Playfield, на котором находится сущность
        /// </summary>
        public string Playfield { get; set; } = string.Empty;

        /// <summary>
        /// Время создания сущности (UTC)
        /// </summary>
        public DateTime SpawnedAt { get; set; }

        /// <summary>
        /// Конструктор по умолчанию
        /// </summary>
        public EntityInfo()
        {
            SpawnedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Типы игровых сущностей в Empyrion
    /// </summary>
    public enum EntityType
    {
        /// <summary>
        /// Неизвестный тип
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Base (BA) - статичная структура
        /// </summary>
        BA = 2,

        /// <summary>
        /// Capital Vessel (CV) - большой корабль
        /// </summary>
        CV = 4,

        /// <summary>
        /// Small Vessel (SV) - малый корабль
        /// </summary>
        SV = 8,

        /// <summary>
        /// Hover Vessel (HV) - ховер
        /// </summary>
        HV = 16,

        /// <summary>
        /// NPC - персонаж
        /// </summary>
        NPC = 64,

        /// <summary>
        /// Player - игрок
        /// </summary>
        Player = 1
    }
}
