using Newtonsoft.Json;

namespace GalacticExpansion.Models
{
    /// <summary>
    /// Представляет ресурсы колонии.
    /// GLEX использует абстрактные "виртуальные ресурсы" вместо реальных игровых предметов.
    /// Это упрощает логику и не требует физического хранения предметов.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class Resources
    {
        /// <summary>
        /// Текущее количество виртуальных ресурсов.
        /// Используется для улучшения базы, создания аванпостов и юнитов.
        /// </summary>
        [JsonProperty("VirtualResources")]
        public float VirtualResources { get; set; }

        /// <summary>
        /// Скорость производства ресурсов (единиц в час).
        /// Зависит от стадии колонии и количества ресурсных аванпостов.
        /// </summary>
        [JsonProperty("ProductionRate")]
        public float ProductionRate { get; set; }

        /// <summary>
        /// Бонус производства от аванпостов (в процентах).
        /// Каждый аванпост добавляет +20%.
        /// </summary>
        [JsonProperty("ProductionBonus")]
        public float ProductionBonus { get; set; }

        /// <summary>
        /// Конструктор по умолчанию
        /// </summary>
        public Resources()
        {
            VirtualResources = 0;
            ProductionRate = 0;
        }

        /// <summary>
        /// Конструктор с начальными значениями
        /// </summary>
        public Resources(float virtualResources, float productionRate)
        {
            VirtualResources = virtualResources;
            ProductionRate = productionRate;
        }

        /// <summary>
        /// Добавляет ресурсы к текущему количеству
        /// </summary>
        public void Add(float amount)
        {
            VirtualResources += amount;
        }

        /// <summary>
        /// Пытается потратить ресурсы. Возвращает true, если хватило ресурсов.
        /// </summary>
        public bool TrySpend(float amount)
        {
            if (VirtualResources >= amount)
            {
                VirtualResources -= amount;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Проверяет, достаточно ли ресурсов для покупки
        /// </summary>
        public bool CanAfford(float amount)
        {
            return VirtualResources >= amount;
        }
    }
}
