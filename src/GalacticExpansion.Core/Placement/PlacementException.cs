using System;

namespace GalacticExpansion.Core.Placement
{
    /// <summary>
    /// Исключение, выбрасываемое при невозможности найти подходящее место для размещения структуры.
    /// Используется PlacementResolver когда все попытки поиска исчерпаны.
    /// </summary>
    public class PlacementException : Exception
    {
        /// <summary>
        /// Название playfield, на котором происходил поиск
        /// </summary>
        public string Playfield { get; }

        /// <summary>
        /// Радиус поиска, в котором не нашлось места
        /// </summary>
        public float SearchRadius { get; }

        /// <summary>
        /// Создает новое исключение PlacementException
        /// </summary>
        public PlacementException(string message) : base(message)
        {
            Playfield = string.Empty;
        }

        /// <summary>
        /// Создает новое исключение PlacementException с деталями поиска
        /// </summary>
        public PlacementException(string message, string playfield, float searchRadius) 
            : base(message)
        {
            Playfield = playfield;
            SearchRadius = searchRadius;
        }

        /// <summary>
        /// Создает новое исключение PlacementException с внутренним исключением
        /// </summary>
        public PlacementException(string message, Exception innerException) 
            : base(message, innerException)
        {
            Playfield = string.Empty;
        }
    }
}
