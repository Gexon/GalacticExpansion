using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GalacticExpansion.Models
{
    /// <summary>
    /// Представляет состояние планеты (playfield) в симуляции.
    /// Отслеживает присутствие игроков, колонии и время последнего обновления структур.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class PlayfieldState
    {
        /// <summary>
        /// Название playfield
        /// </summary>
        [JsonProperty("Name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Есть ли игроки на планете в данный момент
        /// </summary>
        [JsonProperty("HasPlayers")]
        public bool HasPlayers { get; set; }

        /// <summary>
        /// Время последней активности игроков на планете
        /// </summary>
        [JsonProperty("LastPlayerActivity")]
        public DateTime? LastPlayerActivity { get; set; }

        /// <summary>
        /// Список ID колоний на этой планете
        /// </summary>
        [JsonProperty("ColonyIds")]
        public List<string> ColonyIds { get; set; } = new List<string>();

        /// <summary>
        /// Время последнего обновления списка структур на планете
        /// </summary>
        [JsonProperty("LastStructureRefresh")]
        public DateTime? LastStructureRefresh { get; set; }

        /// <summary>
        /// Конструктор по умолчанию
        /// </summary>
        public PlayfieldState()
        {
        }

        /// <summary>
        /// Конструктор с именем планеты
        /// </summary>
        public PlayfieldState(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Отмечает активность игрока на планете
        /// </summary>
        public void MarkPlayerActivity()
        {
            HasPlayers = true;
            LastPlayerActivity = DateTime.UtcNow;
        }

        /// <summary>
        /// Добавляет колонию на планету
        /// </summary>
        public void AddColony(string colonyId)
        {
            if (!ColonyIds.Contains(colonyId))
            {
                ColonyIds.Add(colonyId);
            }
        }

        /// <summary>
        /// Удаляет колонию с планеты
        /// </summary>
        public void RemoveColony(string colonyId)
        {
            ColonyIds.Remove(colonyId);
        }

        /// <summary>
        /// Отмечает обновление списка структур
        /// </summary>
        public void MarkStructureRefresh()
        {
            LastStructureRefresh = DateTime.UtcNow;
        }
    }
}
