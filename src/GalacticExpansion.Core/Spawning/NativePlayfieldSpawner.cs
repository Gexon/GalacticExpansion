using System;
using System.Threading.Tasks;
using Eleon.Modding;
using NLog;
using UnityEngine;
using ILogger = NLog.ILogger; // Явно указываем что используем NLog.ILogger (UnityEngine тоже имеет ILogger)

namespace GalacticExpansion.Core.Spawning
{
    /// <summary>
    /// Реализация нативного спавна через IPlayfield API без ModGameAPI.
    /// Работает ТОЛЬКО в PfServer процессе, где доступен IPlayfield instance.
    /// 
    /// КРИТИЧНО: Этот класс НЕ использует Gateway, SequenceManager или ModGameAPI!
    /// Все операции выполняются синхронно через прямой доступ к IPlayfield.
    /// 
    /// Используется для spawn операций в PfServer процессе при получении IPC команд от Dedi.
    /// </summary>
    public class NativePlayfieldSpawner : INativePlayfieldSpawner
    {
        private readonly IPlayfield _playfield;
        private readonly ILogger _logger;

        /// <summary>
        /// Создает нативный спавнер для указанного playfield.
        /// </summary>
        /// <param name="playfield">IPlayfield instance (получается из IModApi.Application.OnPlayfieldLoaded).</param>
        /// <param name="logger">Логгер для диагностики.</param>
        public NativePlayfieldSpawner(IPlayfield playfield, ILogger logger)
        {
            _playfield = playfield ?? throw new ArgumentNullException(nameof(playfield));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.Info($"[NativeSpawner] Initialized for playfield '{_playfield.Name}'");
        }

        /// <summary>
        /// Спавнит структуру через IPlayfield.SpawnPrefab().
        /// ВАЖНО: IPlayfield.SpawnPrefab() - СИНХРОННЫЙ метод, возвращает EntityId сразу.
        /// </summary>
        /// <param name="prefabName">Название префаба структуры.</param>
        /// <param name="position">Позиция спавна (Unity Vector3).</param>
        /// <param name="rotation">Ротация (Unity Quaternion).</param>
        /// <returns>EntityId созданной структуры.</returns>
        public Task<int> SpawnStructureAsync(string prefabName, Vector3 position, Quaternion rotation)
        {
            if (string.IsNullOrEmpty(prefabName))
                throw new ArgumentException("Prefab name cannot be empty", nameof(prefabName));

            try
            {
                _logger.Info($"[NativeSpawner] Spawning structure '{prefabName}' at {position} (rotation={rotation.eulerAngles})");

                // IPlayfield.SpawnPrefab() принимает только Vector3 (без Quaternion)
                // Для структур rotation обычно применяется через SpawnEntity с полным EntitySpawnInfo
                // Но SpawnPrefab проще и работает для большинства случаев
                var entityId = _playfield.SpawnPrefab(prefabName, position);

                if (entityId > 0)
                {
                    _logger.Info($"[NativeSpawner] ✅ Structure '{prefabName}' spawned successfully (EntityId={entityId})");
                    return Task.FromResult(entityId);
                }

                var error = $"SpawnPrefab returned invalid EntityId={entityId}";
                _logger.Error($"[NativeSpawner] ❌ {error}");
                throw new InvalidOperationException(error);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[NativeSpawner] ❌ Error spawning structure '{prefabName}' at {position}");
                throw;
            }
        }

        /// <summary>
        /// Спавнит NPC через IPlayfield.SpawnEntity().
        /// ВАЖНО: IPlayfield.SpawnEntity() - СИНХРОННЫЙ метод, возвращает EntityId сразу.
        /// Для NPC используется SpawnEntity с entityType string и Quaternion rotation.
        /// </summary>
        /// <param name="entityType">Тип NPC (например, "ZiraxMinigunPatrol").</param>
        /// <param name="position">Позиция спавна (Unity Vector3).</param>
        /// <param name="rotation">Ротация (Unity Quaternion).</param>
        /// <returns>EntityId созданного NPC.</returns>
        public Task<int> SpawnNPCAsync(string entityType, Vector3 position, Quaternion rotation)
        {
            if (string.IsNullOrEmpty(entityType))
                throw new ArgumentException("Entity type cannot be empty", nameof(entityType));

            try
            {
                _logger.Info($"[NativeSpawner] Spawning NPC '{entityType}' at {position} (rotation={rotation.eulerAngles})");

                // IPlayfield.SpawnEntity(string entityType, Vector3 pos, Quaternion rot)
                var entityId = _playfield.SpawnEntity(entityType, position, rotation);

                if (entityId > 0)
                {
                    _logger.Info($"[NativeSpawner] ✅ NPC '{entityType}' spawned successfully (EntityId={entityId})");
                    return Task.FromResult(entityId);
                }

                var error = $"SpawnEntity returned invalid EntityId={entityId}";
                _logger.Error($"[NativeSpawner] ❌ {error}");
                throw new InvalidOperationException(error);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[NativeSpawner] ❌ Error spawning NPC '{entityType}' at {position}");
                throw;
            }
        }

        /// <summary>
        /// Удаляет сущность через IPlayfield.RemoveEntity().
        /// СИНХРОННЫЙ вызов.
        /// </summary>
        /// <param name="entityId">ID сущности для удаления.</param>
        public void RemoveEntity(int entityId)
        {
            if (entityId <= 0)
            {
                _logger.Warn($"[NativeSpawner] Attempted to remove invalid EntityId={entityId}");
                return;
            }

            try
            {
                _logger.Debug($"[NativeSpawner] Removing entity {entityId}");
                _playfield.RemoveEntity(entityId);
                _logger.Info($"[NativeSpawner] ✅ Entity {entityId} removed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[NativeSpawner] Error removing entity {entityId}");
                throw;
            }
        }

        /// <summary>
        /// Смотрит, есть ли id в словаре сущностей этого playfield.
        /// Словарь IPlayfield.Entities — тот же, что в Module_05 §5.2. ContainsKey не бросает, если id нет.
        /// Любой сбой чтения словаря тоже даёт false: проверка существования не должна ронять обработчик IPC.
        /// </summary>
        /// <param name="entityId">ID сущности.</param>
        /// <returns>true, если сущность загружена на этом playfield.</returns>
        public bool EntityExists(int entityId)
        {
            if (entityId <= 0)
                return false;

            try
            {
                var entities = _playfield.Entities;
                var exists = entities != null && entities.ContainsKey(entityId);
                _logger.Debug($"[NativeSpawner] Entity {entityId} exists={exists}");
                return exists;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"[NativeSpawner] Cannot check whether entity {entityId} exists");
                return false;
            }
        }

        /// <summary>
        /// Получает высоту рельефа через IPlayfield.GetTerrainHeightAt().
        /// СИНХРОННЫЙ вызов - работает моментально.
        /// Если рельеф прочитать не удалось, метод бросает исключение.
        /// Раньше здесь возвращались запасные 100 м, и структура повисала в воздухе.
        /// Настоящая земля тоже может быть на высоте 100 м, поэтому ошибку нельзя
        /// отличить по числу 100: её видно только по исключению.
        /// </summary>
        /// <param name="x">Координата X.</param>
        /// <param name="z">Координата Z.</param>
        /// <returns>Высота рельефа в метрах.</returns>
        /// <exception cref="InvalidOperationException">Рельеф в точке прочитать нельзя. Спавн нужно отменить.</exception>
        public float GetTerrainHeight(float x, float z)
        {
            try
            {
                var height = _playfield.GetTerrainHeightAt(x, z);
                _logger.Debug($"[NativeSpawner] Terrain height at ({x}, {z}): {height}m");
                return height;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[NativeSpawner] Error getting terrain height at ({x}, {z})");
                throw new InvalidOperationException(
                    $"Cannot read terrain height at ({x}, {z}). Spawn is aborted so the structure is not placed at the 100m fallback.",
                    ex);
            }
        }
    }
}
