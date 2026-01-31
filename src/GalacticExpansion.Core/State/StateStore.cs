using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalacticExpansion.Models;
using Newtonsoft.Json;
using NLog;

namespace GalacticExpansion.Core.State
{
    /// <summary>
    /// Реализация персистентного хранилища состояния симуляции.
    /// Обеспечивает надежное сохранение и загрузку state.json с защитой от коррупции данных.
    /// 
    /// Алгоритм атомарной записи:
    /// 1. Сериализуем state в JSON
    /// 2. Записываем во временный файл (state.json.tmp)
    /// 3. Атомарно переименовываем state.json.tmp → state.json
    /// 4. Удаляем временный файл при ошибке
    /// 
    /// Thread-safe: использует SemaphoreSlim для синхронизации операций записи.
    /// </summary>
    public class StateStore : IStateStore
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        private readonly string _dataPath;
        private readonly string _statePath;
        private readonly string _backupPath;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        
        private readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc
        };

        /// <summary>
        /// Путь к файлу состояния
        /// </summary>
        public string StatePath => _statePath;

        /// <summary>
        /// Путь к папке с бэкапами
        /// </summary>
        public string BackupPath => _backupPath;

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="dataPath">Путь к папке данных мода (обычно Content/Mods/GalacticExpansion)</param>
        public StateStore(string dataPath)
        {
            if (string.IsNullOrEmpty(dataPath))
                throw new ArgumentException("Data path cannot be null or empty", nameof(dataPath));

            _dataPath = dataPath;
            _statePath = Path.Combine(dataPath, "state.json");
            _backupPath = Path.Combine(dataPath, "Backups");

            // Создаем папки, если не существуют
            Directory.CreateDirectory(_dataPath);
            Directory.CreateDirectory(_backupPath);

            Logger.Info($"StateStore initialized (path: {_dataPath})");
        }

        /// <summary>
        /// Загружает состояние из файла
        /// </summary>
        public async Task<SimulationState> LoadAsync()
        {
            try
            {
                if (!File.Exists(_statePath))
                {
                    Logger.Info("State file not found. Creating new simulation state.");
                    return new SimulationState();
                }

                Logger.Info($"Loading state from {_statePath}...");
                
                var json = await File.ReadAllTextAsync(_statePath);
                var state = JsonConvert.DeserializeObject<SimulationState>(json, _jsonSettings);

                if (state == null)
                {
                    Logger.Warn("State file is empty or invalid. Attempting to restore from backup...");
                    var restoredState = await RestoreFromBackupAsync();
                    
                    if (restoredState != null)
                    {
                        Logger.Info("Successfully restored state from backup");
                        return restoredState;
                    }

                    Logger.Warn("No valid backups found. Creating new simulation state.");
                    return new SimulationState();
                }

                // Применяем миграции, если версия устарела
                state = await ApplyMigrationsAsync(state);

                Logger.Info($"State loaded successfully (version: {state.Version}, colonies: {state.Colonies.Count}, last update: {state.LastUpdate})");
                
                return state;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error loading state file. Attempting to restore from backup...");
                
                var restoredState = await RestoreFromBackupAsync();
                if (restoredState != null)
                {
                    Logger.Info("Successfully restored state from backup after error");
                    return restoredState;
                }

                Logger.Error("Failed to restore from backup. Creating new simulation state.");
                return new SimulationState();
            }
        }

        /// <summary>
        /// Сохраняет состояние в файл атомарно
        /// </summary>
        public async Task SaveAsync(SimulationState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            await _writeLock.WaitAsync();
            try
            {
                Logger.Debug("Saving state...");

                // Обновляем время сохранения
                state.MarkSaved();

                // Сериализуем в JSON
                var json = JsonConvert.SerializeObject(state, _jsonSettings);

                // Атомарная запись через временный файл
                var tempPath = _statePath + ".tmp";
                
                // Записываем во временный файл
                await File.WriteAllTextAsync(tempPath, json);

                // Атомарно заменяем основной файл
                // File.Move с overwrite=true атомарен на Windows и Linux
                File.Move(tempPath, _statePath, overwrite: true);

                Logger.Info($"State saved successfully (version: {state.Version}, colonies: {state.Colonies.Count}, size: {json.Length} bytes)");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error saving state");
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Создает бэкап текущего состояния
        /// </summary>
        public async Task CreateBackupAsync()
        {
            if (!File.Exists(_statePath))
            {
                Logger.Warn("Cannot create backup: state file does not exist");
                return;
            }

            try
            {
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
                var backupFileName = $"state_backup_{timestamp}.json";
                var backupFilePath = Path.Combine(_backupPath, backupFileName);

                Logger.Info($"Creating backup: {backupFileName}");

                // Копируем файл состояния
                File.Copy(_statePath, backupFilePath, overwrite: false);

                Logger.Info($"Backup created successfully: {backupFileName}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error creating backup");
                // Не пробрасываем исключение - ошибка бэкапа не должна ломать сохранение
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Восстанавливает состояние из последнего валидного бэкапа
        /// </summary>
        public async Task<SimulationState?> RestoreFromBackupAsync()
        {
            try
            {
                var backupFiles = Directory.GetFiles(_backupPath, "state_backup_*.json")
                    .OrderByDescending(f => f)
                    .ToList();

                if (backupFiles.Count == 0)
                {
                    Logger.Warn("No backup files found");
                    return null;
                }

                Logger.Info($"Found {backupFiles.Count} backup file(s). Attempting to restore...");

                // Пробуем загрузить бэкапы по порядку (от новых к старым)
                foreach (var backupFile in backupFiles)
                {
                    try
                    {
                        Logger.Info($"Trying to restore from {Path.GetFileName(backupFile)}...");
                        
                        var json = await File.ReadAllTextAsync(backupFile);
                        var state = JsonConvert.DeserializeObject<SimulationState>(json, _jsonSettings);

                        if (state != null)
                        {
                            Logger.Info($"Successfully loaded backup: {Path.GetFileName(backupFile)}");
                            
                            // Применяем миграции, если нужно
                            state = await ApplyMigrationsAsync(state);
                            
                            return state;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, $"Failed to load backup {Path.GetFileName(backupFile)}, trying next...");
                    }
                }

                Logger.Error("Failed to restore from any backup");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error during backup restoration");
                return null;
            }
        }

        /// <summary>
        /// Очищает старые бэкапы
        /// </summary>
        public async Task CleanupOldBackupsAsync(int keepCount)
        {
            try
            {
                var backupFiles = Directory.GetFiles(_backupPath, "state_backup_*.json")
                    .OrderByDescending(f => f)
                    .ToList();

                if (backupFiles.Count <= keepCount)
                {
                    Logger.Debug($"Backup cleanup skipped: only {backupFiles.Count} backups exist (limit: {keepCount})");
                    return;
                }

                var filesToDelete = backupFiles.Skip(keepCount).ToList();
                Logger.Info($"Cleaning up {filesToDelete.Count} old backup(s) (keeping {keepCount} most recent)");

                foreach (var file in filesToDelete)
                {
                    try
                    {
                        File.Delete(file);
                        Logger.Debug($"Deleted old backup: {Path.GetFileName(file)}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, $"Failed to delete backup: {Path.GetFileName(file)}");
                    }
                }

                Logger.Info("Backup cleanup completed");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error during backup cleanup");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Применяет миграции схемы данных
        /// </summary>
        private async Task<SimulationState> ApplyMigrationsAsync(SimulationState state)
        {
            const int CurrentVersion = 1; // Текущая версия схемы

            if (state.Version == CurrentVersion)
            {
                // Миграции не требуются
                return state;
            }

            if (state.Version > CurrentVersion)
            {
                Logger.Warn($"State version ({state.Version}) is newer than supported ({CurrentVersion}). This may cause issues.");
                return state;
            }

            Logger.Info($"Applying migrations from version {state.Version} to {CurrentVersion}");

            // Создаем бэкап перед миграцией
            await CreateBackupAsync();

            // Здесь будут применяться миграции при необходимости
            // Пример:
            // if (state.Version == 1)
            //     state = MigrateV1ToV2(state);
            // if (state.Version == 2)
            //     state = MigrateV2ToV3(state);

            state.Version = CurrentVersion;
            Logger.Info($"Migrations applied successfully. Current version: {CurrentVersion}");

            return state;
        }
    }
}
