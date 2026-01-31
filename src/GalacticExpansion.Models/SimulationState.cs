using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace GalacticExpansion.Models
{
    /// <summary>
    /// Корневая модель состояния симуляции GalacticExpansion.
    /// Содержит все колонии, состояния планет и метаданные симуляции.
    /// Сохраняется в state.json и восстанавливается при перезапуске сервера.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class SimulationState
    {
        /// <summary>
        /// Версия схемы данных.
        /// Используется для миграций при изменении структуры данных.
        /// </summary>
        [JsonProperty("Version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Время создания симуляции
        /// </summary>
        [JsonProperty("CreatedAt")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Время последнего обновления симуляции
        /// </summary>
        [JsonProperty("LastUpdate")]
        public DateTime LastUpdate { get; set; }

        /// <summary>
        /// Время последнего сохранения на диск
        /// </summary>
        [JsonProperty("LastSaveTime")]
        public DateTime LastSaveTime { get; set; }

        /// <summary>
        /// Список всех колоний в симуляции
        /// </summary>
        [JsonProperty("Colonies")]
        public List<Colony> Colonies { get; set; } = new List<Colony>();

        /// <summary>
        /// Состояния планет (индексированы по имени playfield)
        /// </summary>
        [JsonProperty("Playfields")]
        public Dictionary<string, PlayfieldState> Playfields { get; set; } = new Dictionary<string, PlayfieldState>();

        /// <summary>
        /// Флаг, указывающий, что состояние изменилось и требуется сохранение
        /// </summary>
        [JsonIgnore]
        public bool IsDirty { get; set; }

        /// <summary>
        /// Конструктор по умолчанию
        /// </summary>
        public SimulationState()
        {
            var now = DateTime.UtcNow;
            CreatedAt = now;
            LastUpdate = now;
            LastSaveTime = now;
            IsDirty = false;
        }

        /// <summary>
        /// Отмечает, что состояние изменилось
        /// </summary>
        public void MarkDirty()
        {
            IsDirty = true;
            LastUpdate = DateTime.UtcNow;
        }

        /// <summary>
        /// Отмечает, что состояние было сохранено
        /// </summary>
        public void MarkSaved()
        {
            IsDirty = false;
            LastSaveTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Получает или создает состояние планеты
        /// </summary>
        public PlayfieldState GetOrCreatePlayfieldState(string playfieldName)
        {
            if (!Playfields.ContainsKey(playfieldName))
            {
                Playfields[playfieldName] = new PlayfieldState(playfieldName);
                MarkDirty();
            }
            return Playfields[playfieldName];
        }

        /// <summary>
        /// Добавляет колонию в симуляцию
        /// </summary>
        public void AddColony(Colony colony)
        {
            Colonies.Add(colony);
            GetOrCreatePlayfieldState(colony.Playfield).AddColony(colony.Id);
            MarkDirty();
        }

        /// <summary>
        /// Удаляет колонию из симуляции
        /// </summary>
        public void RemoveColony(Colony colony)
        {
            Colonies.Remove(colony);
            if (Playfields.TryGetValue(colony.Playfield, out var playfieldState))
            {
                playfieldState.RemoveColony(colony.Id);
            }
            MarkDirty();
        }

        /// <summary>
        /// Находит колонию по ID
        /// </summary>
        public Colony? FindColony(string colonyId)
        {
            return Colonies.FirstOrDefault(c => c.Id == colonyId);
        }

        /// <summary>
        /// Получает все колонии на указанной планете
        /// </summary>
        public List<Colony> GetColoniesOnPlayfield(string playfieldName)
        {
            return Colonies.Where(c => c.Playfield == playfieldName).ToList();
        }

        /// <summary>
        /// Получает колонию по Entity ID главной структуры
        /// </summary>
        public Colony? FindColonyByStructureId(int structureId)
        {
            return Colonies.FirstOrDefault(c => c.MainStructureId == structureId);
        }

        /// <summary>
        /// Получает общее количество колоний
        /// </summary>
        public int GetTotalColonyCount()
        {
            return Colonies.Count;
        }

        /// <summary>
        /// Получает количество колоний на указанной планете
        /// </summary>
        public int GetColonyCountOnPlayfield(string playfieldName)
        {
            return Colonies.Count(c => c.Playfield == playfieldName);
        }
    }
}
