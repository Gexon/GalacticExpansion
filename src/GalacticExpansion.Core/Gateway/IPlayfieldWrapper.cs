namespace GalacticExpansion.Core.Gateway
{
    /// <summary>
    /// Wrapper-интерфейс для IPlayfield (Empyrion ModAPI).
    /// Изолирует зависимость от Unity и упрощает тестирование.
    /// </summary>
    public interface IPlayfieldWrapper
    {
        /// <summary>
        /// Получает высоту рельефа в указанной точке (X, Z).
        /// Использует IPlayfield.GetTerrainHeightAt() из расширенного API v1.15+.
        /// </summary>
        /// <param name="x">Координата X</param>
        /// <param name="z">Координата Z</param>
        /// <returns>Высота рельефа (Y координата)</returns>
        float GetTerrainHeight(float x, float z);

        /// <summary>
        /// Имя playfield
        /// </summary>
        string Name { get; }
    }
}
