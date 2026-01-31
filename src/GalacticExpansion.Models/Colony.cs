using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GalacticExpansion.Models
{
    /// <summary>
    /// Представляет колонию Zirax на планете.
    /// Колония проходит через несколько стадий развития от посадки до максимального уровня,
    /// строит аванпосты, производит юнитов и реагирует на действия игроков.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class Colony
    {
        /// <summary>
        /// Уникальный идентификатор колонии
        /// </summary>
        [JsonProperty("Id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Имя playfield, на котором находится колония
        /// </summary>
        [JsonProperty("Playfield")]
        public string Playfield { get; set; } = string.Empty;

        /// <summary>
        /// ID фракции (обычно 2 для Zirax)
        /// </summary>
        [JsonProperty("FactionId")]
        public int FactionId { get; set; }

        /// <summary>
        /// Текущая стадия развития колонии
        /// </summary>
        [JsonProperty("Stage")]
        public ColonyStage Stage { get; set; }

        /// <summary>
        /// Позиция главной структуры колонии
        /// </summary>
        [JsonProperty("Position")]
        public Vector3 Position { get; set; } = new Vector3();

        /// <summary>
        /// Поворот главной структуры (в градусах)
        /// </summary>
        [JsonProperty("Rotation")]
        public Vector3 Rotation { get; set; } = new Vector3();

        /// <summary>
        /// Entity ID главной структуры колонии в игре
        /// </summary>
        [JsonProperty("MainStructureId")]
        public int? MainStructureId { get; set; }

        /// <summary>
        /// Ресурсы колонии
        /// </summary>
        [JsonProperty("Resources")]
        public Resources Resources { get; set; } = new Resources();

        /// <summary>
        /// Пул юнитов колонии
        /// </summary>
        [JsonProperty("UnitPool")]
        public UnitPool UnitPool { get; set; } = new UnitPool();

        /// <summary>
        /// Список ресурсных аванпостов
        /// </summary>
        [JsonProperty("ResourceNodes")]
        public List<ResourceNode> ResourceNodes { get; set; } = new List<ResourceNode>();

        /// <summary>
        /// Уровень угрозы (0-5), определяет интенсивность защиты
        /// </summary>
        [JsonProperty("ThreatLevel")]
        public int ThreatLevel { get; set; }

        /// <summary>
        /// Время последнего улучшения колонии
        /// </summary>
        [JsonProperty("LastUpgradeTime")]
        public DateTime? LastUpgradeTime { get; set; }

        /// <summary>
        /// Время последней атаки на колонию
        /// </summary>
        [JsonProperty("LastAttackTime")]
        public DateTime? LastAttackTime { get; set; }

        /// <summary>
        /// История разрушений структур колонии
        /// </summary>
        [JsonProperty("DestructionEvents")]
        public List<DestructionEvent> DestructionEvents { get; set; } = new List<DestructionEvent>();

        /// <summary>
        /// Наличие портала для телепортации (для экспансии)
        /// </summary>
        [JsonProperty("HasPortal")]
        public bool HasPortal { get; set; }

        /// <summary>
        /// Позиция портала (если есть)
        /// </summary>
        [JsonProperty("PortalPosition")]
        public Vector3? PortalPosition { get; set; }

        /// <summary>
        /// Entity ID портала
        /// </summary>
        [JsonProperty("PortalEntityId")]
        public int? PortalEntityId { get; set; }

        /// <summary>
        /// Время создания колонии
        /// </summary>
        [JsonProperty("CreatedAt")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Конструктор по умолчанию
        /// </summary>
        public Colony()
        {
            CreatedAt = DateTime.UtcNow;
            ThreatLevel = 1;
        }

        /// <summary>
        /// Конструктор с параметрами
        /// </summary>
        public Colony(string playfield, int factionId, Vector3 position)
        {
            Playfield = playfield;
            FactionId = factionId;
            Position = position;
            Stage = ColonyStage.LandingPending;
            CreatedAt = DateTime.UtcNow;
            ThreatLevel = 1;
        }

        /// <summary>
        /// Добавляет событие разрушения и увеличивает threat level
        /// </summary>
        public void RecordDestruction(int entityId, string type, int? destroyedBy)
        {
            var destructionEvent = new DestructionEvent(entityId, type, destroyedBy);
            DestructionEvents.Add(destructionEvent);
            LastAttackTime = DateTime.UtcNow;
            
            // Увеличиваем threat level (максимум 5)
            if (ThreatLevel < 5)
            {
                ThreatLevel++;
            }
        }

        /// <summary>
        /// Проверяет, готова ли колония к улучшению
        /// </summary>
        public bool CanUpgrade(float requiredResources, int minTimeSeconds)
        {
            // Проверяем, что колония не на максимальной стадии
            if (Stage >= ColonyStage.BaseMax)
                return false;

            // Проверяем наличие ресурсов
            if (!Resources.CanAfford(requiredResources))
                return false;

            // Проверяем минимальное время с последнего улучшения
            if (LastUpgradeTime.HasValue)
            {
                var timeSinceUpgrade = DateTime.UtcNow - LastUpgradeTime.Value;
                if (timeSinceUpgrade.TotalSeconds < minTimeSeconds)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Выполняет улучшение колонии до следующей стадии
        /// </summary>
        public void Upgrade(float resourceCost)
        {
            if (Resources.TrySpend(resourceCost))
            {
                Stage = Stage.GetNextStage();
                LastUpgradeTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Откатывает колонию на предыдущую стадию (при разрушении главной базы)
        /// </summary>
        public bool Downgrade()
        {
            var previousStage = Stage.GetPreviousStage();
            if (previousStage.HasValue)
            {
                Stage = previousStage.Value;
                MainStructureId = null; // Структура была разрушена
                return true;
            }
            return false;
        }
    }
}
