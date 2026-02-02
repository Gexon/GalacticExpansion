using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Placement
{
    /// <summary>
    /// Критерии размещения структур на планете.
    /// Определяет ограничения по дистанциям, радиус поиска и параметры высоты.
    /// Используется PlacementResolver для поиска подходящих мест спавна.
    /// </summary>
    public class PlacementCriteria
    {
        /// <summary>
        /// Название playfield, на котором происходит поиск
        /// </summary>
        public string Playfield { get; set; } = string.Empty;

        /// <summary>
        /// Минимальная дистанция от игроков (метры).
        /// По умолчанию 500м для избежания спавна рядом с игроками.
        /// </summary>
        public float MinDistanceFromPlayers { get; set; } = 500f;

        /// <summary>
        /// Минимальная дистанция от структур игроков (метры).
        /// По умолчанию 1000м для предотвращения конфликтов территорий.
        /// </summary>
        public float MinDistanceFromPlayerStructures { get; set; } = 1000f;

        /// <summary>
        /// Радиус спирального поиска от центра (метры).
        /// По умолчанию 2000м (2 км).
        /// </summary>
        public float SearchRadius { get; set; } = 2000f;

        /// <summary>
        /// Использовать IPlayfield.GetTerrainHeightAt() для точного определения высоты (API v1.15+).
        /// По умолчанию true - решает проблему спавна под землей.
        /// </summary>
        public bool UseTerrainHeight { get; set; } = true;

        /// <summary>
        /// Отступ над поверхностью земли (метры).
        /// По умолчанию 0.5м - структура слегка приподнята для корректного размещения.
        /// </summary>
        public float HeightOffset { get; set; } = 0.5f;

        /// <summary>
        /// Предпочитаемая позиция для начала поиска (опционально).
        /// Если null - поиск начинается от Vector3.Zero.
        /// </summary>
        public Vector3? PreferredLocation { get; set; }

        /// <summary>
        /// ID фракции для фильтрации структур (обычно 2 для Zirax).
        /// Используется для проверки дистанций только от чужих структур.
        /// </summary>
        public int FactionId { get; set; } = 2;

        /// <summary>
        /// Максимальное время поиска (секунды).
        /// По умолчанию 5 секунд для предотвращения зависания.
        /// </summary>
        public int MaxSearchTimeSeconds { get; set; } = 5;
    }
}
