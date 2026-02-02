using System;
using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Spawning
{
    /// <summary>
    /// Исключение, выбрасываемое при ошибке спавна игровой сущности.
    /// Содержит детали о prefab и позиции для диагностики проблемы.
    /// </summary>
    public class SpawnException : Exception
    {
        /// <summary>
        /// Название prefab, который не удалось заспавнить
        /// </summary>
        public string PrefabName { get; }

        /// <summary>
        /// Позиция, на которой пытались заспавнить сущность
        /// </summary>
        public Vector3 Position { get; }

        /// <summary>
        /// Создает новое исключение SpawnException
        /// </summary>
        public SpawnException(string message) : base(message)
        {
            PrefabName = string.Empty;
            Position = new Vector3();
        }

        /// <summary>
        /// Создает новое исключение SpawnException с деталями спавна
        /// </summary>
        public SpawnException(string message, string prefabName, Vector3 position) 
            : base(message)
        {
            PrefabName = prefabName;
            Position = position;
        }

        /// <summary>
        /// Создает новое исключение SpawnException с внутренним исключением
        /// </summary>
        public SpawnException(string message, Exception innerException) 
            : base(message, innerException)
        {
            PrefabName = string.Empty;
            Position = new Vector3();
        }
    }
}
