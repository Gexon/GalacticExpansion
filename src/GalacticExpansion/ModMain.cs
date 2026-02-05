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
    /// Реализует интерфейсы ModInterface (базовый API) и IMod (расширенный API) для взаимодействия с Empyrion Dedicated Server.
    /// 
    /// Жизненный цикл на Dedicated Server:
    /// 1. Game_Start(ModGameAPI dediAPI) - базовая инициализация с ограниченным API
    /// 2. Init(IModApi modAPI) - расширенная инициализация с доступом к IApplication и IPlayfield
    /// 3. Game_Event() / Game_Update() - обработка событий и обновление симуляции каждый tick
    /// 4. Game_Exit() / Shutdown() - остановка мода
    /// 
    /// IMod.Init предоставляет доступ к IModApi.Application.OnPlayfieldLoaded для получения IPlayfield объектов
    /// и использования IPlayfield.GetTerrainHeightAt() для точного определения высоты рельефа.
    /// </summary>
    public class ModMain : ModInterface, IMod
    {
        private static ILogger? _logger;
        private ServiceContainer? _container;
        private IEmpyrionGateway? _gateway;
        private IStateStore? _stateStore;
        private ISimulationEngine? _simulationEngine;
        private SimulationState? _currentState;
        private Configuration? _config;
        private ModGameAPI? _modApi;
        private IModApi? _extendedModApi; // Расширенный API (IMod.Init) для доступа к IApplication и IPlayfield
        
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
                _logger.Info("GalacticExpansion (GLEX) v1.0 Phase 3");
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

                // 7. Инициализируем Phase 2/3: Core Loop и доменные компоненты
                _logger.Info("Initializing Phase 2/3 components...");
                
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
                // IModApi в Game_Start ещё нет (появляется в IMod.Init); передаём null, в Init вызовем SetModApi
                var placementResolver = new PlacementResolver(_gateway, playerTracker, _logger, modApi: null);
                _container.Register<IPlacementResolver>(placementResolver);
                _logger.Info("PlacementResolver registered (terrain height: will use IPlayfield after IMod.Init, fallback 100m until then)");

                
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

                // ColonyTickModule — обновление колоний по тику и создание первой виртуальной колонии.
                // Материализация виртуальных колоний происходит через IModApi.OnPlayfieldLoaded (обрабатывается в IMod.Init).
                var colonyTickModule = new ColonyTickModule(colonyManager, placementResolver, eventBus, _config, _logger);
                _simulationEngine.RegisterModule(colonyTickModule);
                _logger.Info("ColonyTickModule registered");
                
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
        /// В Phase 3 SimulationEngine управляет основным циклом через собственный таймер.
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

                // Таймауты при shutdown: Game_Exit вызывается из главного потока игры; бесконечный .Wait()
                // приводит к тому, что сервер не может закрыться. При таймауте продолжаем выход.
                const int simulationShutdownMs = 15000;
                const int backupShutdownMs = 10000;

                // 1. Останавливаем SimulationEngine (сохранит state и завершит модули)
                if (_simulationEngine != null && _simulationEngine.IsRunning)
                {
                    _logger?.Info("Stopping SimulationEngine...");
                    var simStopTask = _simulationEngine.StopAsync();
                    if (!simStopTask.Wait(simulationShutdownMs))
                    {
                        _logger?.Warn($"SimulationEngine did not stop within {simulationShutdownMs}ms. Proceeding with shutdown.");
                    }
                    else
                    {
                        _logger?.Info("SimulationEngine stopped");
                    }
                }

                // 2. Останавливаем Gateway (внутри уже есть таймаут 5s для RequestQueue)
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
                    var backupTask = _stateStore.CreateBackupAsync();
                    if (!backupTask.Wait(backupShutdownMs))
                    {
                        _logger?.Warn($"Final backup did not complete within {backupShutdownMs}ms. Proceeding with shutdown.");
                    }
                    else
                    {
                        _logger?.Info("Backup created");
                    }
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

        #region IMod Implementation (Extended API)

        /// <summary>
        /// Инициализация расширенного API мода (IMod.Init).
        /// Вызывается Dedicated Server после Game_Start для предоставления доступа к IModApi.
        /// 
        /// IModApi предоставляет:
        /// - IApplication.OnPlayfieldLoaded/OnPlayfieldUnloading события для кэширования IPlayfield объектов
        /// - IPlayfield.GetTerrainHeightAt() для точного определения высоты рельефа
        /// - Другие расширенные возможности API
        /// 
        /// Примечание: Этот метод вызывается на том же Dedicated Server процессе после Game_Start.
        /// </summary>
        public void Init(IModApi modAPI)
        {
            try
            {
                if (_logger == null)
                {
                    InitializeLogging();
                    _logger = LogManager.GetCurrentClassLogger();
                }

                _logger.Info("========================================");
                _logger.Info("GalacticExpansion IMod.Init - Extended API initialization...");
                _logger.Info("========================================");

                // Сохраняем ссылку на IModApi для доступа к расширенным возможностям
                _extendedModApi = modAPI;

                // Поздняя инъекция IModApi в PlacementResolver (в Game_Start API ещё недоступен)
                if (_container != null && _container.TryResolve<IPlacementResolver>(out var placementResolver) && placementResolver != null)
                {
                    placementResolver.SetModApi(modAPI);
                    _logger.Info("✅ IModApi passed to PlacementResolver - terrain height via IPlayfield.GetTerrainHeightAt() enabled");
                }
                else
                {
                    _logger.Info("✅ IModApi initialized (PlacementResolver not resolved, terrain may use fallback)");
                }

                // Подписка на событие загрузки playfield (правильный способ для детектирования готовности playfield)
                // OnPlayfieldLoaded срабатывает когда playfield полностью загружен и готов для операций спавна
                if (modAPI.Application != null)
                {
                    modAPI.Application.OnPlayfieldLoaded += OnPlayfieldLoaded;
                    _logger.Info("✅ Subscribed to IModApi.Application.OnPlayfieldLoaded event");
                }
                else
                {
                    _logger.Warn("⚠️ IModApi.Application is null - cannot subscribe to OnPlayfieldLoaded");
                }
            }
            catch (Exception ex)
            {
                var logger = _logger ?? LogManager.GetCurrentClassLogger();
                logger.Error(ex, "Error during IMod.Init (extended API initialization)");
            }
        }

        /// <summary>
        /// Обработчик события OnPlayfieldLoaded из IModApi.Application.
        /// Вызывается когда playfield полностью загружен и готов для операций (спавн структур, и т.д.).
        /// Это ПРАВИЛЬНОЕ событие для материализации колоний (в отличие от Event_Playfield_Loaded который срабатывает слишком рано).
        /// </summary>
        private void OnPlayfieldLoaded(IPlayfield playfield)
        {
            try
            {
                if (playfield == null)
                {
                    _logger?.Warn("OnPlayfieldLoaded: playfield is null");
                    return;
                }

                var playfieldName = playfield.Name;
                _logger?.Info($"🎯 IModApi.OnPlayfieldLoaded: '{playfieldName}' is now READY for spawn operations");

                // Получаем ColonyManager из контейнера для материализации колоний
                if (_container != null && _container.TryResolve<IColonyManager>(out var colonyManager) && colonyManager != null)
                {
                    // Запускаем материализацию виртуальных колоний асинхронно
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await colonyManager.EnsurePlayfieldColoniesSpawnedAsync(playfieldName);
                        }
                        catch (Exception ex)
                        {
                            _logger?.Error(ex, $"Error ensuring colonies spawned on playfield '{playfieldName}'");
                        }
                    });
                }
                else
                {
                    _logger?.Warn("OnPlayfieldLoaded: ColonyManager not resolved from container");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error in OnPlayfieldLoaded handler");
            }
        }

        /// <summary>
        /// Остановка мода (IMod.Shutdown).
        /// Вызывается Dedicated Server при остановке мода.
        /// </summary>
        public void Shutdown()
        {
            _logger?.Info("IMod.Shutdown called");
            
            // Отписываемся от события OnPlayfieldLoaded
            if (_extendedModApi?.Application != null)
            {
                _extendedModApi.Application.OnPlayfieldLoaded -= OnPlayfieldLoaded;
                _logger?.Info("Unsubscribed from IModApi.Application.OnPlayfieldLoaded event");
            }
            
            // Используем тот же метод остановки что и Game_Exit
            Game_Exit();
        }

        #endregion
    }
}
