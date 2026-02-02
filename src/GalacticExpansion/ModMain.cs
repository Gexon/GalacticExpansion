using System;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Economy;
using GalacticExpansion.Core.Gateway;
using GalacticExpansion.Core.Placement;
using GalacticExpansion.Core.Simulation;
using GalacticExpansion.Core.Spawning;
using GalacticExpansion.Core.State;
using GalacticExpansion.Core.Tracking;
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
    /// 3. Game_Update() - вызывается каждый tick (используется SimulationEngine)
    /// 4. Shutdown() - остановка при выключении сервера
    /// </summary>
    public class ModMain : ModInterface
    {
        private static ILogger? _logger;
        private ServiceContainer? _container;
        private IEmpyrionGateway? _gateway;
        private IStateStore? _stateStore;
        private ISimulationEngine? _simulationEngine;
        private SimulationState? _currentState;
        private Configuration? _config;
        private ModGameAPI? _modApi;
        
        private DateTime _lastBackupTime;
        private bool _isInitialized = false; // Флаг инициализации

        /// <summary>
        /// Инициализация мода.
        /// Вызывается при запуске dedicated server.
        /// </summary>
        public void Game_Start(ModGameAPI dediAPI)
        {
            try
            {
                // Защита от повторной инициализации
                if (_isInitialized)
                {
                    var logger = _logger ?? LogManager.GetCurrentClassLogger();
                    logger.Warn("Game_Start called again, but mod is already initialized. Skipping.");
                    return;
                }
                
                // 1. Инициализация логирования
                InitializeLogging();
                _logger = LogManager.GetCurrentClassLogger();
                
                _logger.Info("========================================");
                _logger.Info("GalacticExpansion (GLEX) v1.0 Phase 2");
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
                
                // Регистрируем логгер
                _container.Register<ILogger>(_logger!);
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

                // 7. Инициализируем Phase 2: Core Loop компоненты
                _logger.Info("Initializing Phase 2 components...");
                
                // EventBus для внутренней коммуникации модулей
                var eventBus = new EventBus(_logger);
                _container.Register<IEventBus>(eventBus);
                _logger.Info("EventBus initialized");
                
                // ModuleRegistry для управления модулями
                var moduleRegistry = new ModuleRegistry(_logger);
                _container.Register<IModuleRegistry>(moduleRegistry);
                _logger.Info("ModuleRegistry initialized");
                
                // SimulationEngine - главный движок симуляции
                _simulationEngine = new SimulationEngine(
                    _stateStore,
                    moduleRegistry,
                    eventBus,
                    _logger
                );
                _container.Register<ISimulationEngine>(_simulationEngine);
                
                // 8. Регистрируем модули симуляции
                _logger.Info("Registering simulation modules...");
                
                // PlayerTracker - отслеживание игроков
                var playerTracker = new PlayerTracker(_gateway, eventBus, _logger);
                _simulationEngine.RegisterModule(playerTracker);
                _container.Register<IPlayerTracker>(playerTracker);
                _logger.Info("PlayerTracker registered");
                
                // StructureTracker - отслеживание структур
                var structureTracker = new StructureTracker(_gateway, eventBus, _logger);
                _simulationEngine.RegisterModule(structureTracker);
                _container.Register<IStructureTracker>(structureTracker);
                _logger.Info("StructureTracker registered");
                
                // Регистрируем Phase 3 Domain модули
                _logger.Info("Registering Phase 3 domain modules...");
                
                // PlacementResolver - поиск мест для структур
                var placementResolver = new PlacementResolver(_gateway, playerTracker, _logger);
                _container.Register<IPlacementResolver>(placementResolver);
                _logger.Info("PlacementResolver registered");
                
                // EntitySpawner - спавн структур и NPC
                var entitySpawner = new EntitySpawner(_gateway, placementResolver, _logger);
                _container.Register<IEntitySpawner>(entitySpawner);
                _logger.Info("EntitySpawner registered");
                
                // EconomySimulator - виртуальная экономика
                var economySimulator = new EconomySimulator(_config, _logger);
                _container.Register<IEconomySimulator>(economySimulator);
                _logger.Info("EconomySimulator registered");
                
                // UnitEconomyManager - управление юнитами
                var unitEconomyManager = new UnitEconomyManager(_config, _logger);
                _container.Register<IUnitEconomyManager>(unitEconomyManager);
                _logger.Info("UnitEconomyManager registered");
                
                // StageManager - управление стадиями колоний
                var stageManager = new StageManager(
                    _gateway,
                    entitySpawner,
                    placementResolver,
                    economySimulator,
                    unitEconomyManager,
                    _stateStore,
                    eventBus,
                    _config,
                    _logger
                );
                _container.Register<IStageManager>(stageManager);
                _logger.Info("StageManager registered");
                
                // ColonyManager - координация модулей
                var colonyManager = new ColonyManager(
                    stageManager,
                    economySimulator,
                    unitEconomyManager,
                    _stateStore,
                    _logger
                );
                // ColonyManager не является модулем симуляции, только координатором
                _container.Register<IColonyManager>(colonyManager);
                _logger.Info("ColonyManager registered");
                
                // 9. Запускаем симуляцию
                _logger.Info("Starting simulation engine...");
                _ = Task.Run(async () => await _simulationEngine.StartAsync());
                
                // Даем время на инициализацию
                Task.Delay(500).Wait();
                
                // Получаем текущее состояние из движка
                _currentState = _simulationEngine.State;

                // 10. Инициализируем таймеры
                _lastBackupTime = DateTime.UtcNow;

                // 11. Логируем успешную инициализацию
                _logger.Info("========================================");
                _logger.Info("GLEX initialized successfully!");
                _logger.Info($"  Home Playfield: {_config.HomePlayfield}");
                _logger.Info($"  Expansion: {(_config.EnableExpansion ? "Enabled" : "Disabled")}");
                _logger.Info($"  Tick Interval: {_config.Simulation.TickIntervalMs}ms");
                _logger.Info($"  Auto-save: every {_config.Simulation.SaveIntervalMinutes} minute(s)");
                _logger.Info("========================================");
                
                // Устанавливаем флаг успешной инициализации
                _isInitialized = true;
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
        /// В Phase 2 SimulationEngine управляет основным циклом через собственный таймер.
        /// Здесь остается только периодическое создание бэкапов.
        /// </summary>
        public void Game_Update()
        {
            try
            {
                if (_config == null || _stateStore == null)
                    return;

                var now = DateTime.UtcNow;

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

                // 1. Останавливаем SimulationEngine (сохранит state и завершит модули)
                if (_simulationEngine != null && _simulationEngine.IsRunning)
                {
                    _logger?.Info("Stopping SimulationEngine...");
                    _simulationEngine.StopAsync().Wait();
                    _logger?.Info("SimulationEngine stopped");
                }

                // 2. Останавливаем Gateway
                if (_gateway != null && _gateway.IsRunning)
                {
                    _logger?.Info("Stopping Gateway...");
                    _gateway.Stop();
                    _logger?.Info("Gateway stopped");
                }

                // 3. Создаем финальный бэкап
                if (_stateStore != null)
                {
                    _logger?.Info("Creating final backup...");
                    _stateStore.CreateBackupAsync().Wait();
                    _logger?.Info("Backup created");
                }

                _logger?.Info("========================================");
                _logger?.Info("GLEX shutdown complete");
                _logger?.Info("========================================");

                // Сбрасываем флаг инициализации
                _isInitialized = false;

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
