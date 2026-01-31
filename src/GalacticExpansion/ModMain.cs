using System;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Gateway;
using GalacticExpansion.Core.State;
using GalacticExpansion.DependencyInjection;
using GalacticExpansion.Models;
using NLog;
using NLog.Config;

namespace GalacticExpansion
{
    /// <summary>
    /// Главная точка входа мода GalacticExpansion.
    /// Реализует интерфейс ModInterface для взаимодействия с Empyrion.
    /// 
    /// Жизненный цикл:
    /// 1. Init() - инициализация при запуске сервера
    /// 2. Game_Event() - обработка событий от игры
    /// 3. Game_Update() - вызывается каждый tick (будет использоваться в Phase 2)
    /// 4. Shutdown() - остановка при выключении сервера
    /// </summary>
    public class ModMain : ModInterface
    {
        private static ILogger? _logger;
        private ServiceContainer? _container;
        private IEmpyrionGateway? _gateway;
        private IStateStore? _stateStore;
        private SimulationState? _currentState;
        private Configuration? _config;
        private ModGameAPI? _modApi;
        
        private DateTime _lastSaveTime;
        private DateTime _lastBackupTime;

        /// <summary>
        /// Инициализация мода.
        /// Вызывается при запуске dedicated server.
        /// </summary>
        public void Game_Start(ModGameAPI dediAPI)
        {
            try
            {
                // 1. Инициализация логирования
                InitializeLogging();
                _logger = LogManager.GetCurrentClassLogger();
                
                _logger.Info("========================================");
                _logger.Info("GalacticExpansion (GLEX) v1.0 Phase 1");
                _logger.Info("Initializing...");
                _logger.Info("========================================");

                // 2. Определяем путь к папке мода
                // AppDomain.CurrentDomain.BaseDirectory = "D:\...\DedicatedServer\"
                // Нужно подняться на уровень выше, чтобы попасть в корень игры
                var gameRoot = System.IO.Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
                var modPath = System.IO.Path.Combine(
                    gameRoot,
                    "Content", "Mods", "GalacticExpansion"
                );
                _logger.Info($"Mod path: {modPath}");

                // Сохраняем ссылку на ModGameAPI
                _modApi = dediAPI;

                // 3. Загружаем конфигурацию
                _logger.Info("Loading configuration...");
                var configLoader = new ConfigurationLoader(modPath);
                _config = configLoader.Load();

                // Обновляем уровень логирования из конфига
                UpdateLogLevel(_config.LogLevel);

                // 4. Создаем DI контейнер и регистрируем сервисы
                _logger.Info("Setting up dependency injection...");
                _container = new ServiceContainer();
                
                // Регистрируем логгер (явно проверяем на null для компилятора)
                if (_logger != null)
                    _container.Register<ILogger>(_logger);
                
                _container.Register<ModGameAPI>(_modApi);
                _container.Register<Configuration>(_config);

                // 5. Инициализируем Gateway
                _logger.Info("Initializing Empyrion Gateway...");
                _gateway = new EmpyrionGateway(
                    _modApi, 
                    _config.Limits.MaxRequestsPerSecond
                );
                _container.Register<IEmpyrionGateway>(_gateway);
                _gateway.Start();
                _logger.Info($"Gateway started (rate limit: {_config.Limits.MaxRequestsPerSecond} req/sec)");

                // 6. Инициализируем StateStore
                _logger.Info("Initializing State Store...");
                _stateStore = new StateStore(modPath);
                _container.Register<IStateStore>(_stateStore);

                // 7. Загружаем состояние симуляции
                _logger.Info("Loading simulation state...");
                _currentState = _stateStore.LoadAsync().Result;
                _logger.Info($"State loaded: {_currentState.Colonies.Count} colonies, version {_currentState.Version}");

                // 8. Инициализируем таймеры
                _lastSaveTime = DateTime.UtcNow;
                _lastBackupTime = DateTime.UtcNow;

                // 9. Логируем успешную инициализацию
                _logger.Info("========================================");
                _logger.Info("GLEX initialized successfully!");
                _logger.Info($"  Home Playfield: {_config.HomePlayfield}");
                _logger.Info($"  Expansion: {(_config.EnableExpansion ? "Enabled" : "Disabled")}");
                _logger.Info($"  Tick Interval: {_config.Simulation.TickIntervalMs}ms");
                _logger.Info($"  Auto-save: every {_config.Simulation.SaveIntervalMinutes} minute(s)");
                _logger.Info("========================================");
            }
            catch (Exception ex)
            {
                // Критическая ошибка при инициализации
                var logger = _logger ?? LogManager.GetCurrentClassLogger();
                logger.Fatal(ex, "FATAL ERROR during initialization! Mod will not function properly.");
                throw; // Пробрасываем исключение, чтобы Empyrion знал о проблеме
            }
        }

        /// <summary>
        /// Обработка событий от игры.
        /// Вызывается когда Empyrion отправляет события моду.
        /// </summary>
        public void Game_Event(CmdId eventId, ushort seqNr, object data)
        {
            try
            {
                // Передаем событие в Gateway для обработки
                _gateway?.HandleEvent(eventId, seqNr, data);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Error handling game event: {eventId} (SeqNr: {seqNr})");
            }
        }

        /// <summary>
        /// Обновление симуляции.
        /// Вызывается каждый тик сервера.
        /// В Phase 1 используется только для авто-сохранения.
        /// В Phase 2 здесь будет основной цикл симуляции.
        /// </summary>
        public void Game_Update()
        {
            try
            {
                if (_config == null || _stateStore == null || _currentState == null)
                    return;

                var now = DateTime.UtcNow;

                // Авто-сохранение состояния
                if (_currentState.IsDirty && 
                    (now - _lastSaveTime).TotalMinutes >= _config.Simulation.SaveIntervalMinutes)
                {
                    _logger?.Debug("Auto-saving state...");
                    _stateStore.SaveAsync(_currentState).Wait();
                    _lastSaveTime = now;
                }

                // Создание периодических бэкапов
                if ((now - _lastBackupTime).TotalHours >= _config.Simulation.StateBackupIntervalHours)
                {
                    _logger?.Info("Creating periodic backup...");
                    _stateStore.CreateBackupAsync().Wait();
                    _stateStore.CleanupOldBackupsAsync(_config.Simulation.KeepBackupCount).Wait();
                    _lastBackupTime = now;
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error in Game_Update");
            }
        }

        /// <summary>
        /// Graceful shutdown мода.
        /// Вызывается при остановке dedicated server.
        /// </summary>
        public void Game_Exit()
        {
            try
            {
                _logger?.Info("========================================");
                _logger?.Info("GLEX shutting down...");
                _logger?.Info("========================================");

                // 1. Останавливаем Gateway
                if (_gateway != null && _gateway.IsRunning)
                {
                    _logger?.Info("Stopping Gateway...");
                    _gateway.Stop();
                    _logger?.Info("Gateway stopped");
                }

                // 2. Сохраняем финальное состояние
                if (_stateStore != null && _currentState != null)
                {
                    _logger?.Info("Saving final state...");
                    _stateStore.SaveAsync(_currentState).Wait();
                    _logger?.Info("State saved");

                    // 3. Создаем финальный бэкап
                    _logger?.Info("Creating final backup...");
                    _stateStore.CreateBackupAsync().Wait();
                    _logger?.Info("Backup created");
                }

                _logger?.Info("========================================");
                _logger?.Info("GLEX shutdown complete");
                _logger?.Info("========================================");

                // Flush логов
                LogManager.Flush();
            }
            catch (Exception ex)
            {
                _logger?.Fatal(ex, "Error during shutdown!");
            }
        }

        /// <summary>
        /// Инициализирует систему логирования NLog.
        /// 
        /// NLog не может автоматически найти конфиг, потому что мод загружается из Content/Mods/,
        /// но AppDomain.CurrentDomain.BaseDirectory указывает на DedicatedServer\.
        /// Поэтому нужно явно указать путь к NLog.config в папке мода.
        /// </summary>
        private void InitializeLogging()
        {
            try
            {
                // Определяем путь к папке мода
                // AppDomain.CurrentDomain.BaseDirectory = "D:\...\DedicatedServer\"
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var gameRoot = System.IO.Path.GetDirectoryName(baseDir);
                var modPath = System.IO.Path.Combine(gameRoot, "Content", "Mods", "GalacticExpansion");
                var nlogConfigPath = System.IO.Path.Combine(modPath, "NLog.config");

                // Пробуем загрузить конфигурацию из NLog.config в папке мода
                if (System.IO.File.Exists(nlogConfigPath))
                {
                    LogManager.Configuration = new XmlLoggingConfiguration(nlogConfigPath);
                }
                else
                {
                    // Конфиг не найден - используем минимальную программную конфигурацию (только консоль)
                    var config = new LoggingConfiguration();
                    var consoleTarget = new NLog.Targets.ConsoleTarget("console")
                    {
                        Layout = "${time}|${level:uppercase=true:truncate=5}|${logger:shortName=true}|${message}"
                    };
                    config.AddRule(LogLevel.Debug, LogLevel.Fatal, consoleTarget);
                    LogManager.Configuration = config;
                    
                    // Выводим предупреждение в консоль
                    var tempLogger = LogManager.GetCurrentClassLogger();
                    tempLogger.Warn($"NLog.config not found at: {nlogConfigPath}");
                    tempLogger.Warn("Using console-only logging configuration");
                }
            }
            catch (Exception ex)
            {
                // Критическая ошибка при инициализации NLog - используем fallback конфигурацию
                var config = new LoggingConfiguration();
                var consoleTarget = new NLog.Targets.ConsoleTarget("console")
                {
                    Layout = "${time}|${level:uppercase=true:truncate=5}|${logger:shortName=true}|${message}"
                };
                config.AddRule(LogLevel.Debug, LogLevel.Fatal, consoleTarget);
                LogManager.Configuration = config;
                
                var tempLogger = LogManager.GetCurrentClassLogger();
                tempLogger.Error(ex, "Failed to initialize NLog configuration");
            }
        }

        /// <summary>
        /// Обновляет уровень логирования
        /// </summary>
        private void UpdateLogLevel(string logLevel)
        {
            var level = LogLevel.FromString(logLevel);
            
            foreach (var rule in LogManager.Configuration.LoggingRules)
            {
                rule.EnableLoggingForLevel(level);
            }

            LogManager.ReconfigExistingLoggers();
            _logger?.Info($"Log level set to: {logLevel}");
        }
    }
}
