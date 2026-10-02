using System.Threading.Tasks;
using UnityEngine;

namespace GalacticExpansion.Core.Spawning
{
    /// <summary>
    /// Интерфейс для нативного спавна через IPlayfield API (без ModGameAPI).
    /// Работает только в PfServer процессе, где доступен прямой IPlayfield instance.
    /// 
    /// Преимущества по сравнению с Gateway+EntitySpawner:
    /// - НЕ требует ModGameAPI
    /// - Синхронные вызовы (быстрее)
    /// - Прямой доступ к playfield entities
    /// - Простая архитектура без промежуточных слоев
    /// </summary>
    public interface INativePlayfieldSpawner
    {
        /// <summary>
        /// Спавнит структуру через IPlayfield.SpawnPrefab().
        /// СИНХРОННЫЙ вызов - возвращает EntityId сразу.
        /// </summary>
        /// <param name="prefabName">Название префаба структуры (например, "GLEX_Base_L1").</param>
        /// <param name="position">Позиция спавна (Unity Vector3).</param>
        /// <param name="rotation">Ротация (Unity Quaternion).</param>
        /// <returns>EntityId созданной структуры.</returns>
        Task<int> SpawnStructureAsync(string prefabName, Vector3 position, Quaternion rotation);

        /// <summary>
        /// Спавнит NPC через IPlayfield.SpawnEntity().
        /// СИНХРОННЫЙ вызов - возвращает EntityId сразу.
        /// </summary>
        /// <param name="entityType">Тип NPC (например, "ZiraxMinigunPatrol").</param>
        /// <param name="position">Позиция спавна (Unity Vector3).</param>
        /// <param name="rotation">Ротация (Unity Quaternion).</param>
        /// <returns>EntityId созданного NPC.</returns>
        Task<int> SpawnNPCAsync(string entityType, Vector3 position, Quaternion rotation);

        /// <summary>
        /// Удаляет сущность через IPlayfield.RemoveEntity().
        /// СИНХРОННЫЙ вызов.
        /// </summary>
        /// <param name="entityId">ID сущности для удаления.</param>
        void RemoveEntity(int entityId);

        /// <summary>
        /// Получает высоту рельефа через IPlayfield.GetTerrainHeightAt().
        /// СИНХРОННЫЙ вызов.
        /// Если рельеф прочитать нельзя, бросает исключение и не подставляет запасные 100 м.
        /// </summary>
        /// <param name="x">Координата X.</param>
        /// <param name="z">Координата Z.</param>
        /// <returns>Высота рельефа в метрах.</returns>
        float GetTerrainHeight(float x, float z);
    }
}
