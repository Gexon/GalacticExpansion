using Eleon.Modding;

namespace GalacticExpansion.Core.Gateway
{
    /// <summary>
    /// Реальная реализация wrapper для IPlayfield.
    /// Используется в production коде для доступа к расширенному API.
    /// </summary>
    public class PlayfieldWrapper : IPlayfieldWrapper
    {
        private readonly IPlayfield _playfield;

        /// <summary>
        /// Создаёт wrapper вокруг реального IPlayfield
        /// </summary>
        public PlayfieldWrapper(IPlayfield playfield)
        {
            _playfield = playfield ?? throw new System.ArgumentNullException(nameof(playfield));
        }

        /// <summary>
        /// Получает высоту рельефа через IPlayfield.GetTerrainHeightAt()
        /// </summary>
        public float GetTerrainHeight(float x, float z)
        {
            return _playfield.GetTerrainHeightAt(x, z);
        }

        /// <summary>
        /// Имя playfield из IPlayfield.Name
        /// </summary>
        public string Name => _playfield.Name;
    }
}
