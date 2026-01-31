using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GalacticExpansion.Models
{
    /// <summary>
    /// Представляет пул юнитов колонии.
    /// Реализует систему конечности резервов: юниты производятся со временем,
    /// а не спавнятся бесконечно.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class UnitPool
    {
        /// <summary>
        /// Доступные охранники для спавна
        /// </summary>
        [JsonProperty("AvailableGuards")]
        public int AvailableGuards { get; set; }

        /// <summary>
        /// Максимальная вместимость гарнизона (зависит от стадии)
        /// </summary>
        [JsonProperty("MaxGuards")]
        public int MaxGuards { get; set; }

        /// <summary>
        /// Доступные патрульные корабли
        /// </summary>
        [JsonProperty("AvailablePatrolVessels")]
        public int AvailablePatrolVessels { get; set; }

        /// <summary>
        /// Максимальное количество патрульных кораблей
        /// </summary>
        [JsonProperty("MaxPatrolVessels")]
        public int MaxPatrolVessels { get; set; }

        /// <summary>
        /// Доступные боевые корабли
        /// </summary>
        [JsonProperty("AvailableWarships")]
        public int AvailableWarships { get; set; }

        /// <summary>
        /// Максимальное количество боевых кораблей
        /// </summary>
        [JsonProperty("MaxWarships")]
        public int MaxWarships { get; set; }

        /// <summary>
        /// Доступные дроны для волн атак
        /// </summary>
        [JsonProperty("AvailableDrones")]
        public int AvailableDrones { get; set; }

        /// <summary>
        /// Максимальное количество дронов
        /// </summary>
        [JsonProperty("MaxDrones")]
        public int MaxDrones { get; set; }

        /// <summary>
        /// Скорость производства юнитов (единиц в час)
        /// </summary>
        [JsonProperty("ProductionRate")]
        public float ProductionRate { get; set; }

        /// <summary>
        /// Последнее время производства юнитов
        /// </summary>
        [JsonProperty("LastProductionTime")]
        public DateTime LastProductionTime { get; set; }

        /// <summary>
        /// Накопленный прогресс производства (дробная часть юнита)
        /// </summary>
        [JsonProperty("ProductionProgress")]
        public float ProductionProgress { get; set; }

        /// <summary>
        /// Список активных юнитов в игре
        /// </summary>
        [JsonProperty("ActiveUnits")]
        public List<ActiveUnit> ActiveUnits { get; set; }

        /// <summary>
        /// Конструктор по умолчанию
        /// </summary>
        public UnitPool()
        {
            AvailableGuards = 0;
            MaxGuards = 0;
            AvailablePatrolVessels = 0;
            MaxPatrolVessels = 0;
            AvailableWarships = 0;
            MaxWarships = 0;
            AvailableDrones = 0;
            MaxDrones = 0;
            ProductionRate = 0;
            LastProductionTime = DateTime.UtcNow;
            ProductionProgress = 0;
            ActiveUnits = new List<ActiveUnit>();
        }

        /// <summary>
        /// Пытается получить охранника из пула. Возвращает true, если охранник доступен.
        /// </summary>
        public bool TryConsumeGuard()
        {
            if (AvailableGuards > 0)
            {
                AvailableGuards--;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Пытается получить патрульный корабль из пула
        /// </summary>
        public bool TryConsumePatrolVessel()
        {
            if (AvailablePatrolVessels > 0)
            {
                AvailablePatrolVessels--;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Пытается получить боевой корабль из пула
        /// </summary>
        public bool TryConsumeWarship()
        {
            if (AvailableWarships > 0)
            {
                AvailableWarships--;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Пытается получить дронов для волны атаки
        /// </summary>
        public bool TryConsumeDrones(int count)
        {
            if (AvailableDrones >= count)
            {
                AvailableDrones -= count;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Добавляет охранника в пул
        /// </summary>
        public void ReturnGuard()
        {
            if (AvailableGuards < MaxGuards)
            {
                AvailableGuards++;
            }
        }

        /// <summary>
        /// Регистрирует активного юнита
        /// </summary>
        public void RegisterActiveUnit(ActiveUnit unit)
        {
            ActiveUnits.Add(unit);
        }

        /// <summary>
        /// Удаляет активного юнита (при смерти или деспавне)
        /// </summary>
        public void UnregisterActiveUnit(int entityId)
        {
            ActiveUnits.RemoveAll(u => u.EntityId == entityId);
        }
    }

    /// <summary>
    /// Представляет активного юнита в игре
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ActiveUnit
    {
        /// <summary>
        /// Entity ID в игре
        /// </summary>
        [JsonProperty("EntityId")]
        public int EntityId { get; set; }

        /// <summary>
        /// Тип юнита
        /// </summary>
        [JsonProperty("Type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Время спавна
        /// </summary>
        [JsonProperty("SpawnedAt")]
        public DateTime SpawnedAt { get; set; }

        /// <summary>
        /// Назначенная роль (Guard, Patrol, Hunter, Wave)
        /// </summary>
        [JsonProperty("AssignedRole")]
        public string? AssignedRole { get; set; }

        public ActiveUnit()
        {
            SpawnedAt = DateTime.UtcNow;
        }

        public ActiveUnit(int entityId, string type, string? role = null)
        {
            EntityId = entityId;
            Type = type;
            AssignedRole = role;
            SpawnedAt = DateTime.UtcNow;
        }
    }
}
