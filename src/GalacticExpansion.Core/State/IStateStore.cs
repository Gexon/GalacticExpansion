using System;
using System.Threading.Tasks;
using GalacticExpansion.Models;

namespace GalacticExpansion.Core.State
{
    /// <summary>
    /// Интерфейс для персистентного хранилища состояния симуляции.
    /// Обеспечивает сохранение и загрузку state.json с атомарной записью,
    /// автоматическими бэкапами и системой миграций.
    /// 
    /// Ключевые особенности:
    /// - Атомарная запись (temp file + atomic rename)
    /// - Автоматические бэкапы (периодические + перед миграциями)
    /// - Система версионирования и миграций
    /// - Восстановление из бэкапа при коррупции данных
    /// - Thread-safe операции
    /// </summary>
    public interface IStateStore
    {
        /// <summary>
        /// Загружает состояние из файла.
        /// Если файл поврежден, пытается восстановить из последнего бэкапа.
        /// Если файл не существует, создает новое состояние.
        /// </summary>
        /// <returns>Загруженное состояние симуляции</returns>
        Task<SimulationState> LoadAsync();

        /// <summary>
        /// Сохраняет состояние в файл атомарно.
        /// Использует временный файл и атомарное переименование для предотвращения коррупции.
        /// </summary>
        /// <param name="state">Состояние для сохранения</param>
        Task SaveAsync(SimulationState state);

        /// <summary>
        /// Создает бэкап текущего состояния.
        /// </summary>
        Task CreateBackupAsync();

        /// <summary>
        /// Восстанавливает состояние из последнего валидного бэкапа.
        /// </summary>
        /// <returns>Восстановленное состояние или null, если бэкапы не найдены</returns>
        Task<SimulationState?> RestoreFromBackupAsync();

        /// <summary>
        /// Очищает старые бэкапы, оставляя только последние N.
        /// </summary>
        /// <param name="keepCount">Количество бэкапов для сохранения</param>
        Task CleanupOldBackupsAsync(int keepCount);

        /// <summary>
        /// Получает путь к файлу состояния
        /// </summary>
        string StatePath { get; }

        /// <summary>
        /// Получает путь к папке с бэкапами
        /// </summary>
        string BackupPath { get; }
    }
}
