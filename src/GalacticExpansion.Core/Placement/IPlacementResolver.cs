using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Placement
{
    /// <summary>
    /// Интерфейс для поиска подходящих мест размещения структур на планетах.
    /// Реализует спиральный алгоритм поиска с проверкой дистанций от игроков и структур,
    /// а также точное определение высоты рельефа через IPlayfield.GetTerrainHeightAt() (API v1.15+).
    /// </summary>
    public interface IPlacementResolver
    {
        /// <summary>
        /// Находит подходящее место для размещения структуры согласно критериям.
        /// Использует спиральный алгоритм поиска от центра с адаптивным шагом.
        /// </summary>
        /// <param name="criteria">Критерии размещения (дистанции, радиус поиска и т.д.)</param>
        /// <returns>Позиция для спавна структуры с корректной высотой</returns>
        /// <exception cref="PlacementException">Выбрасывается, если не найдено подходящее место</exception>
        Task<Vector3> FindSuitableLocationAsync(PlacementCriteria criteria);

        /// <summary>
        /// Проверяет, подходит ли конкретная позиция для размещения структуры.
        /// Проверяет дистанции от игроков и существующих структур.
        /// </summary>
        /// <param name="position">Позиция для проверки</param>
        /// <param name="criteria">Критерии размещения</param>
        /// <returns>true, если позиция подходит; false, если нарушены ограничения</returns>
        Task<bool> IsLocationSuitableAsync(Vector3 position, PlacementCriteria criteria);

        /// <summary>
        /// Точно определяет высоту рельефа на планете (API v1.15+).
        /// Решает проблему спавна структур под землей или в воздухе.
        /// </summary>
        /// <param name="playfield">Интерфейс playfield из ModAPI</param>
        /// <param name="x">X координата</param>
        /// <param name="z">Z координата</param>
        /// <returns>Высота поверхности земли в метрах</returns>
        float GetTerrainHeight(IPlayfield playfield, float x, float z);

        /// <summary>
        /// Находит позицию на рельефе с указанным смещением по высоте.
        /// Использует GetTerrainHeightAt() для точного размещения на поверхности.
        /// </summary>
        /// <param name="playfieldName">Название playfield</param>
        /// <param name="x">X координата</param>
        /// <param name="z">Z координата</param>
        /// <param name="heightOffset">Смещение над поверхностью (метры), по умолчанию 0.5м</param>
        /// <returns>Позиция на рельефе с корректной высотой</returns>
        /// <exception cref="System.ArgumentException">Выбрасывается, если playfield не найден</exception>
        Task<Vector3> FindLocationAtTerrainAsync(
            string playfieldName,
            float x,
            float z,
            float heightOffset = 0.5f
        );
    }
}
