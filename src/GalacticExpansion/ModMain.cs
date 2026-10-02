using System;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Economy;
using GalacticExpansion.Core.Gateway;
using GalacticExpansion.Core.IPC;
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
        private static string? _processPrefix; // Префикс процесса для логов [Dedi-PID] или [PfServer-PID]
        private ServiceContainer? _container;
        private IEmpyrionGateway? _gateway;
        private IStateStore? _stateStore;
        private ISimulationEngine? _simulationEngine;
        private IColonyManager? _colonyManager; // Для материализации в Dedi процессе
        private SimulationState? _currentState;
        private Configuration? _config;
        private ModGameAPI? _modApi;
        private IModApi? _extendedModApi; // Расширенный API (IMod.Init) для доступа к IApplication и IPlayfield
        
        // IPC для multi-process архитектуры (Dedi <-> PfServer коммуникация)
        private NetworkBridge? _networkBridge;
        private ApplicationMode _processMode;
        private string? _currentPlayfield; // Для PfServer процесса - название плейфилда
        
        private DateTime _lastBackupTime;
        private DateTime _lastMaterializationAttempt = DateTime.MinValue; // Для throttling материализации (Dedi)
        private volatile bool _materializationInProgress = false; // Защита от параллельных вызовов материализации
        private bool _isInitialized = false; // Флаг инициализации
        private bool _simulationStarted = false; // Флаг запуска симуляции (для отложенного старта)

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

                // Проверка: если dediAPI null, это single-player или клиент
                if (_modApi == null)
                {
                    _logger.Warn("Game_Start called with null ModGameAPI - likely single-player or client mode");
                    _logger.Warn("Mod will use IModApi from IMod.Init instead");
                }

                // 3. Загружаем конфигурацию
                _logger.Info("Loading configuration...");
                var configLoader = new ConfigurationLoader(modPath);
                _config = configLoader.Load();

                // Обновляем уровень логирования из конфига
                UpdateLogLevel(_config.LogLevel);

                // 4. Создаем DI контейнер и регистрируем сервисы
                _logger.Info("Setting up dependency injection...");
                _container = new ServiceContainer();
                
                // Регистрируем логгер и конфигурацию
                _container.Register<ILogger>(_logger!);
                _container.Register<Configuration>(_config);
                
                // Регистрируем ModGameAPI только если доступен
                if (_modApi != null)
                {
                    _container.Register<ModGameAPI>(_modApi);
                }

                // 5. ОТЛОЖЕННАЯ инициализация - НЕ создаем Gateway и модули в Game_Start
                // Полная инициализация произойдет в IMod.Init() когда будет доступен IModApi
                _logger.Info("Skipping Gateway and modules initialization - will initialize in IMod.Init");
                _logger.Info("Deferred initialization mode - waiting for IMod.Init with IModApi");
                
                // 6. Инициализируем только StateStore (нужен для сохранения состояния)
                _logger.Info("Initializing State Store...");
                _stateStore = new StateStore(modPath);
                _container.Register<IStateStore>(_stateStore);
                
                // Устанавливаем флаг базовой инициализации
                _logger.Info("========================================");
                _logger.Info("GLEX basic initialization complete. Waiting for IMod.Init...");
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
                // КРИТИЧЕСКИ ВАЖНО для PfServer: Event_Playfield_Loaded сообщает о loaded playfield
                // Используем это событие для завершения инициализации NetworkBridge в PfServer процессе
                if (_processMode == ApplicationMode.PlayfieldServer && eventId == CmdId.Event_Playfield_Loaded)
                {
                    if (data is PlayfieldLoad pfLoad && !string.IsNullOrEmpty(pfLoad.playfield))
                    {
                        _logger?.Info($"[PfServer] Event_Playfield_Loaded: {pfLoad.playfield}");
                        
                        // Устанавливаем текущий playfield
                        _currentPlayfield = pfLoad.playfield;
                        
                        // Завершаем инициализацию NetworkBridge (теперь знаем playfield name)
                        if (_networkBridge != null && !string.IsNullOrEmpty(_currentPlayfield))
                        {
                            _networkBridge.InitializeForPlayfieldServer(_currentPlayfield);
                            _logger?.Info($"✅ [PfServer] NetworkBridge fully initialized for '{_currentPlayfield}'");
                        }
                    }
                }
                
                // Передаем событие в Gateway для обработки (если Gateway инициализирован)
                _gateway?.HandleEvent(eventId, seqNr, data);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Error handling game event: {eventId} (SeqNr: {seqNr})");
            }
        }

        /// <summary>
        /// Обновление мода на каждом game tick.
        /// КРИТИЧЕСКИ ВАЖНО: Game_Update вызывается в ОБОИХ процессах (Dedi и PfServer).
        ///
        /// В Dedi процессе:
        /// - Материализация виртуальных колоний через IPC (отправка команд в PfServer)
        /// - Периодические бэкапы состояния
        /// 
        /// В PfServer процессе:
        /// - Минимальная логика (IPC команды обрабатываются асинхронно через NetworkBridge)
        /// </summary>
        public void Game_Update()
        {
            try
            {
                if (!_isInitialized)
                    return;

                // РАЗНАЯ ЛОГИКА для разных процессов
                if (_processMode == ApplicationMode.DedicatedServer)
                {
                    // ==== DEDI ПРОЦЕСС ====
                    
                    // Материализация виртуальных колоний через IPC (fire-and-forget на ThreadPool).
                    // КРИТИЧНО: НЕ вызывать .GetAwaiter().GetResult() — это вызывает deadlock!
                    // Continuation от await внутри TryMaterializePendingColoniesAsync захватывает
                    // SynchronizationContext (Unity) и пытается вернуться на main thread,
                    // который заблокирован GetResult(). Результат — deadlock навсегда.
                    //
                    // Throttle: не чаще раза в 3 секунды, чтобы не спамить IPC-запросами.
                    // Guard _materializationInProgress: защита от параллельных Task.Run.
                    if (_simulationStarted && _colonyManager != null && !_materializationInProgress)
                    {
                        var now = DateTime.UtcNow;
                        if ((now - _lastMaterializationAttempt).TotalSeconds >= 3.0)
                        {
                            _lastMaterializationAttempt = now;
                            _materializationInProgress = true;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await _colonyManager.TryMaterializePendingColoniesAsync();
                                }
                                catch (Exception ex)
                                {
                                    _logger?.Debug($"[Dedi] Materialization check: {ex.Message}");
                                }
                                finally
                                {
                                    _materializationInProgress = false;
                                }
                            });
                        }
                    }
                    
                    // Периодические бэкапы (только в Dedi)
                    if (_config != null && _stateStore != null)
                    {
                        var now = DateTime.UtcNow;
                        if ((now - _lastBackupTime).TotalHours >= _config.Simulation.StateBackupIntervalHours)
                        {
                            _logger?.Info("[Dedi] Creating periodic backup...");
                            _stateStore.CreateBackupAsync().Wait();
                            _stateStore.CleanupOldBackupsAsync(_config.Simulation.KeepBackupCount).Wait();
                            _lastBackupTime = now;
                        }
                    }
                }
                else if (_processMode == ApplicationMode.PlayfieldServer)
                {
                    // ==== PFSERVER ПРОЦЕСС ====
                    // Минимальная логика - IPC обрабатывается асинхронно через NetworkBridge callbacks
                    // Spawn операции выполняются в HandleIPCRequestAsync по запросам от Dedi
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[{_processMode}] Error in Game_Update");
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
        /// 
        /// Также определяет процесс (Dedi/PfServer) для префикса в логах.
        /// </summary>
        private void InitializeLogging()
        {
            try
            {
                // Определяем тип процесса для логов (Dedi vs PfServer)
                var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
                var commandLine = Environment.CommandLine.ToLowerInvariant();
                var processType = commandLine.Contains("-playfieldserver") ? "PfServer" : "Dedi";
                _processPrefix = $"[{processType}-{processId}]";

                // Устанавливаем в GlobalDiagnosticsContext для использования в NLog layout
                NLog.GlobalDiagnosticsContext.Set("process", _processPrefix);

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
                        Layout = "${time}|${level:uppercase=true:truncate=5}|${logger:shortName=true}|${gdc:item=process}|${message}"
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
                    Layout = "${time}|${level:uppercase=true:truncate=5}|${logger:shortName=true}|${gdc:item=process}|${message}"
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
        /// КРИТИЧЕСКИ ВАЖНО: IMod.Init вызывается в КАЖДОМ процессе (Dedi и PfServer).        /// Определяем режим процесса и инициализируем соответственно:
        /// - ApplicationMode.DedicatedServer: полная инициализация (симуляция, логика, IPC отправка команд)
        /// - ApplicationMode.PlayfieldServer: легковесная инициализация (IPC прием команд, spawn)
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

                // Определяем режим процесса через IModApi.Application.Mode
                _processMode = modAPI.Application.Mode;
                _extendedModApi = modAPI;

                _logger.Info("========================================");
                _logger.Info($"GalacticExpansion IMod.Init - Process Mode: {_processMode}");
                _logger.Info("========================================");

                // Разные пути инициализации для разных процессов
                if (_processMode == ApplicationMode.DedicatedServer)
                {
                    _logger.Info("Initializing as DEDICATED SERVER (full simulation + IPC sender)");
                    InitializeDedicatedServer(modAPI);
                }
                else if (_processMode == ApplicationMode.PlayfieldServer)
                {
                    _logger.Info("Initializing as PLAYFIELD SERVER (IPC receiver + spawn executor)");
                    InitializePlayfieldServer(modAPI);
                }
                else
                {
                    _logger.Warn($"Unknown process mode: {_processMode} - skipping initialization");
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                var logger = _logger ?? LogManager.GetCurrentClassLogger();
                logger.Fatal(ex, "CRITICAL ERROR during IMod.Init");
            }
        }

        /// <summary>
        /// Инициализация для Dedicated Server процесса.
        /// КРИТИЧНО: NetworkBridge и IPCEntitySpawner создаются ДО модулей, чтобы весь граф зависимостей был правильным!
        /// </summary>
        private void InitializeDedicatedServer(IModApi modAPI)
        {
            try
            {
                // Извлекаем ModGameAPI из IModApi если нужно
                if (_modApi == null)
                {
                    _logger?.Info("Extracting ModGameAPI from IModApi for Dedi process...");
                    var gameApiProperty = modAPI.GetType().GetProperty("GameAPI") ?? modAPI.GetType().GetProperty("API");
                    if (gameApiProperty != null)
                    {
                        var gameApi = gameApiProperty.GetValue(modAPI) as ModGameAPI;
                        if (gameApi != null)
                        {
                            _logger?.Info("✅ ModGameAPI extracted successfully");
                            _modApi = gameApi;
                        }
                        else
                        {
                            _logger?.Error("❌ Failed to extract ModGameAPI - cannot initialize");
                            return;
                        }
                    }
                    else
                    {
                        _logger?.Error("❌ IModApi does not contain GameAPI property");
                        return;
                    }
                }

                // КРИТИЧНО: NetworkBridge создаем ПЕРВЫМ (до InitializeGatewayAndModules)
                _logger?.Info("Initializing NetworkBridge for Dedi process...");
                var ipcChannel = new EmpyrionModChannel(modAPI.Network);
                _logger?.Info($"IPC channel '{ipcChannel.ChannelId}' (вызовы ModApi.Network из сборки мода)");
                _networkBridge = new NetworkBridge(ipcChannel, _logger ?? LogManager.GetCurrentClassLogger());
                _networkBridge.InitializeForDedi();
                _logger?.Info("✅ NetworkBridge initialized for Dedi (can send commands to PfServer)");

                // Подписка на PlayfieldReadyNotification от PfServer.
                // Когда PfServer сообщает что playfield загружен, помечаем виртуальные колонии
                // на этом playfield для материализации (PendingMaterialization = true).
                _networkBridge.OnPlayfieldReadyReceived += (playfield) =>
                {
                    _logger?.Info($"[Dedi] Received PlayfieldReadyNotification from PfServer for '{playfield}'");
                    if (_simulationStarted && _colonyManager != null)
                    {
                        _ = _colonyManager.EnsurePlayfieldColoniesSpawnedAsync(playfield);
                    }
                    else
                    {
                        _logger?.Debug($"[Dedi] Simulation not started yet, ignoring PlayfieldReady for '{playfield}'");
                    }
                };

                // Инициализируем базовые компоненты (Gateway, StateStore, EventBus, ModuleRegistry)
                // EntitySpawner на Dedi НЕ создаётся — спавн идёт ТОЛЬКО через IPC к PfServer
                InitializeGatewayAndModulesForDedi(modAPI);

                // Поздняя инъекция IModApi в PlacementResolver
                if (_container != null && _container.TryResolve<IPlacementResolver>(out var placementResolver2) && placementResolver2 != null)
                {
                    placementResolver2.SetModApi(modAPI);
                    _logger?.Info("✅ IModApi injected into PlacementResolver");
                }
            }
            catch (Exception ex)
            {
                _logger?.Fatal(ex, "CRITICAL ERROR during Dedi initialization");
                throw;
            }
        }

        /// <summary>
        /// Инициализация Gateway и модулей специально для Dedi процесса.
        /// Создает IPCEntitySpawner (без EntitySpawner!) ДО создания StageManager и других модулей.
        /// </summary>
        private void InitializeGatewayAndModulesForDedi(IModApi modAPI)
        {
            try
            {
                if (_logger == null)
                    throw new InvalidOperationException("Logger must be initialized");
                if (_networkBridge == null)
                    throw new InvalidOperationException("NetworkBridge must be initialized before modules");

                var logger = _logger;

                // Определяем путь к моду
                if (_config == null)
                {
                    var gameRoot = System.IO.Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
                    var modPath = System.IO.Path.Combine(gameRoot, "Content", "Mods", "GalacticExpansion");
                    
                    logger.Info($"Loading configuration from: {modPath}");
                    var configLoader = new ConfigurationLoader(modPath);
                    _config = configLoader.Load();
                    UpdateLogLevel(_config.LogLevel);
                }

                // Создаем контейнер если нужно
                if (_container == null)
                {
                    _container = new ServiceContainer();
                    _container.Register<ILogger>(logger);
                    _container.Register<Configuration>(_config);
                }

                // Регистрируем ModGameAPI
                if (_modApi != null)
                    _container.Register<ModGameAPI>(_modApi);

                // Gateway - защита от двойной инициализации
                if (_gateway == null)
                {
                    if (_modApi == null)
                    {
                        logger.Error("Cannot initialize Gateway - ModGameAPI is null");
                        throw new InvalidOperationException("ModGameAPI required");
                    }

                    logger.Info("Initializing Empyrion Gateway...");
                    _gateway = new EmpyrionGateway(_modApi, _config.Limits.MaxRequestsPerSecond);
                    _container.Register<IEmpyrionGateway>(_gateway);
                    _gateway.Start();
                    logger.Info($"Gateway started (rate limit: {_config.Limits.MaxRequestsPerSecond} req/sec)");
                }
                else
                {
                    logger.Debug("Gateway already initialized, skipping");
                }
                if (_stateStore == null)
                {
                    var gameRoot = System.IO.Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
                    var modPath = System.IO.Path.Combine(gameRoot, "Content", "Mods", "GalacticExpansion");
                    
                    logger.Info("Initializing State Store...");
                    _stateStore = new StateStore(modPath);
                    _container.Register<IStateStore>(_stateStore);
                }

                // EventBus
                var eventBus = new EventBus(logger);
                _container.Register<IEventBus>(eventBus);
                logger.Info("EventBus initialized");
                
                // ModuleRegistry
                var moduleRegistry = new ModuleRegistry(logger);
                _container.Register<IModuleRegistry>(moduleRegistry);
                logger.Info("ModuleRegistry initialized");
                
                // SimulationEngine
                _simulationEngine = new SimulationEngine(_stateStore, moduleRegistry, eventBus, logger);
                _container.Register<ISimulationEngine>(_simulationEngine);
                
                // Регистрируем модули симуляции
                logger.Info("Registering simulation modules...");
                
                // PlayerTracker
                var playerTracker = new PlayerTracker(_gateway, eventBus, logger);
                _simulationEngine.RegisterModule(playerTracker);
                _container.Register<IPlayerTracker>(playerTracker);
                logger.Info("PlayerTracker registered");
                
                // StructureTracker
                var structureTracker = new StructureTracker(_gateway, eventBus, logger);
                _simulationEngine.RegisterModule(structureTracker);
                _container.Register<IStructureTracker>(structureTracker);
                logger.Info("StructureTracker registered");
                
                // ===== КРИТИЧНО: Создаем IPCEntitySpawner ДО StageManager/ColonyManager =====
                // АРХИТЕКТУРНОЕ ПРАВИЛО: На Dedi EntitySpawner НЕ создаётся!
                // Спавн через ModAPI Gateway на Dedi не работает (Empyrion отвечает Event_Ok без entity ID).
                // Все spawn-операции идут ТОЛЬКО через IPC к PfServer.
                logger.Info("Creating IPCEntitySpawner for Dedi process (IPC only, no EntitySpawner)...");
                
                // PlacementResolver — работает на Dedi через IModApi для определения terrain height
                var placementResolver = new PlacementResolver(_gateway, playerTracker, logger, modAPI);
                _container.Register<IPlacementResolver>(placementResolver);
                logger.Info("PlacementResolver registered");
                
                // IPCEntitySpawner для Dedi: NetworkBridge + PlacementResolver, БЕЗ EntitySpawner
                var ipcSpawner = new IPCEntitySpawner(
                    _networkBridge, // IPC к PfServer
                    placementResolver, // Terrain height на Dedi
                    ApplicationMode.DedicatedServer,
                    logger
                );
                _container.Register<IEntitySpawner>(ipcSpawner);
                logger.Info("✅ IPCEntitySpawner registered (IPC only, EntitySpawner excluded from Dedi)");
                
                // Теперь создаем модули которые используют IEntitySpawner - они получат IPCEntitySpawner!
                var economySimulator = new EconomySimulator(_config, logger);
                _container.Register<IEconomySimulator>(economySimulator);
                logger.Info("EconomySimulator registered");
                
                var unitEconomyManager = new UnitEconomyManager(_config, logger);
                _container.Register<IUnitEconomyManager>(unitEconomyManager);
                logger.Info("UnitEconomyManager registered");
                
                // StageManager получает IPCEntitySpawner!
                var stageManager = new StageManager(
                    _gateway,
                    ipcSpawner, // <-- IPC spawner с самого начала!
                    placementResolver,
                    economySimulator,
                    unitEconomyManager,
                    _stateStore,
                    eventBus,
                    _config,
                    logger
                );
                _container.Register<IStageManager>(stageManager);
                logger.Info("StageManager registered with IPCEntitySpawner");
                
                // ColonyManager получает StageManager с IPCEntitySpawner
                var colonyManager = new ColonyManager(
                    _gateway,
                    stageManager,
                    economySimulator,
                    unitEconomyManager,
                    _stateStore,
                    logger
                );
                _colonyManager = colonyManager;
                _container.Register<IColonyManager>(colonyManager);
                logger.Info("ColonyManager registered");

                // ColonyTickModule получает ColonyManager с правильным графом
                var colonyTickModule = new ColonyTickModule(_gateway, colonyManager, placementResolver, eventBus, _config, logger);
                _simulationEngine.RegisterModule(colonyTickModule);
                logger.Info("ColonyTickModule registered");
                
                // Запускаем симуляцию сразу после инициализации всех модулей.
                // Симуляция работает с виртуальными колониями и не требует загруженного playfield.
                // Материализация колоний произойдёт позже по IPC-уведомлению PlayfieldReadyNotification от PfServer.
                // Это позволяет колониям жить автономно, независимо от подключения игроков.
                logger.Info("Starting SimulationEngine immediately after initialization...");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _simulationEngine.StartAsync();
                        
                        // Минимальная пауза для завершения инициализации модулей (InitializeAsync)
                        await Task.Delay(500);
                        
                        _currentState = _simulationEngine.State;
                        _lastBackupTime = DateTime.UtcNow;
                        _simulationStarted = true;

                        // Передаём in-memory state в ColonyManager, чтобы он работал
                        // напрямую с тем же state, что и SimulationEngine (без чтения файла)
                        if (_colonyManager != null && _currentState != null)
                        {
                            _colonyManager.SetSimulationState(_currentState);
                        }
                        
                        _logger?.Info("========================================");
                        _logger?.Info("[Dedi] GLEX simulation started successfully!");
                        _logger?.Info($"  Home Playfield: {_config?.HomePlayfield}");
                        _logger?.Info($"  Tick Interval: {_config?.Simulation.TickIntervalMs}ms");
                        _logger?.Info("========================================");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, "Failed to start SimulationEngine");
                    }
                });

                logger.Info("========================================");
                logger.Info("GLEX Dedi process initialized successfully!");
                logger.Info($"  Home Playfield: {_config.HomePlayfield}");
                logger.Info($"  IPC Mode: ENABLED (spawn via NetworkBridge)");
                logger.Info($"  Simulation: STARTING (immediate, no playfield dependency)");
                logger.Info("========================================");
            }
            catch (Exception ex)
            {
                _logger?.Fatal(ex, "FATAL ERROR during Dedi modules initialization");
                throw;
            }
        }

        /// <summary>
        /// Инициализация для PlayfieldServer процесса.
        /// НОВАЯ АРХИТЕКТУРА: Использует NativePlayfieldSpawner для прямого спавна через IPlayfield API.
        /// НЕ требует ModGameAPI - работает через прямой доступ к IPlayfield instance.
        /// КРИТИЧНО: Подписка на OnPlayfieldLoaded для получения IPlayfield и создания NativePlayfieldSpawner.
        /// </summary>
        private void InitializePlayfieldServer(IModApi modAPI)
        {
            try
            {
                _logger?.Info("PfServer initialization - NATIVE playfield spawn mode (no ModGameAPI required)");

                // КРИТИЧНО: Подписываемся на OnPlayfieldLoaded для получения IPlayfield instance
                // Это единственный способ получить прямой доступ к playfield для нативного спавна
                modAPI.Application.OnPlayfieldLoaded += (pfInstance) =>
                {
                    try
                    {
                        // Проверяем что pfInstance не null перед использованием
                        if (pfInstance == null)
                        {
                            _logger?.Warn("[PfServer] OnPlayfieldLoaded called with null IPlayfield instance");
                            return;
                        }

                        // Получаем название playfield из IPlayfield instance
                        var pfName = pfInstance.Name ?? "Unknown";
                        _logger?.Info($"[PfServer] OnPlayfieldLoaded: {pfName} (IPlayfield instance received)");
                        
                        // Сохраняем название playfield
                        _currentPlayfield = pfName;
                        
                        // КРИТИЧНО: Создаем NativePlayfieldSpawner с прямым доступом к IPlayfield
                        // Это полностью заменяет Gateway + EntitySpawner + PlacementResolver в PfServer!
                        var nativeSpawner = new NativePlayfieldSpawner(pfInstance, _logger ?? LogManager.GetCurrentClassLogger());
                        _logger?.Info($"✅ [PfServer] NativePlayfieldSpawner created for '{pfName}' (direct IPlayfield access)");
                        
                        // Завершаем инициализацию NetworkBridge (теперь знаем playfield)
                        if (_networkBridge != null && !string.IsNullOrEmpty(_currentPlayfield))
                        {
                            _networkBridge.InitializeForPlayfieldServer(_currentPlayfield);
                            _logger?.Info($"✅ [PfServer] NetworkBridge fully initialized for '{_currentPlayfield}' (ready for IPC)");
                            
                            // Регистрируем обработчик IPC запросов с нативным спавнером
                            _networkBridge.OnRequestReceived += async (request, pfName) => 
                            {
                                return await HandleIPCRequestWithNativeSpawner(request, pfName, nativeSpawner);
                            };
                            _logger?.Info($"✅ [PfServer] IPC handler registered with NativePlayfieldSpawner");

                            // Уведомляем Dedi что playfield полностью загружен и готов к spawn-операциям.
                            // Dedi получит это и вызовет EnsurePlayfieldColoniesSpawnedAsync -> PendingMaterialization = true.
                            var readyNotification = new GalacticExpansion.Core.IPC.PlayfieldReadyNotification
                            {
                                Playfield = _currentPlayfield
                            };
                            _networkBridge.SendNotificationToDedi(readyNotification);
                            _logger?.Info($"✅ [PfServer] Sent PlayfieldReadyNotification to Dedi for '{_currentPlayfield}'");
                        }
                        else
                        {
                            _logger?.Error($"[PfServer] Cannot initialize NetworkBridge: bridge={_networkBridge != null}, playfield='{_currentPlayfield}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, "[PfServer] Error in OnPlayfieldLoaded handler");
                    }
                };
                _logger?.Info("✅ [PfServer] Subscribed to OnPlayfieldLoaded");

                // Минимальная конфигурация (для логирования)
                if (_config == null)
                {
                    var gameRoot = System.IO.Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
                    var modPath = System.IO.Path.Combine(gameRoot, "Content", "Mods", "GalacticExpansion");
                    var configLoader = new ConfigurationLoader(modPath);
                    _config = configLoader.Load();
                }

                // Инициализируем NetworkBridge для приема IPC команд от Dedi
                // ВАЖНО: название playfield и IPC handler будут установлены в OnPlayfieldLoaded
                _logger?.Info("Initializing NetworkBridge for PfServer...");
                var ipcChannel = new EmpyrionModChannel(modAPI.Network);
                _logger?.Info($"IPC channel '{ipcChannel.ChannelId}' (вызовы ModApi.Network из сборки мода)");
                _networkBridge = new NetworkBridge(ipcChannel, _logger ?? LogManager.GetCurrentClassLogger());
                
                // ВАЖНО: InitializeForPlayfieldServer и OnRequestReceived будут вызваны из OnPlayfieldLoaded
                _logger?.Info("⏳ NetworkBridge created, waiting for OnPlayfieldLoaded to complete registration");
            }
            catch (Exception ex)
            {
                _logger?.Fatal(ex, "CRITICAL ERROR during PfServer initialization");
                throw;
            }
        }

        /// <summary>
        /// Обработчик IPC запросов от Dedi процесса (вызывается в PfServer).
        /// КРИТИЧНО: Жесткая проверка playfield перед выполнением операций!
        /// </summary>
        private async Task<IPCMessage?> HandleIPCRequestAsync(IPCMessage request, string playfieldName)
        {
            try
            {
                _logger?.Info($"[PfServer] Handling IPC request: {request.MessageType} (playfield: {playfieldName})");

                if (request is SpawnStructureRequest spawnReq)
                {
                    // КРИТИЧНО: Жесткая проверка playfield!
                    // Если не совпадает - возвращаем ошибку, НЕ продолжаем!
                    if (spawnReq.Playfield != _currentPlayfield)
                    {
                        var error = $"Playfield mismatch! Requested={spawnReq.Playfield}, Current={_currentPlayfield}. Cannot spawn in wrong playfield!";
                        _logger?.Error($"[PfServer] ❌ {error}");
                        return new SpawnStructureResponse
                        {
                            Success = false,
                            ErrorMessage = error,
                            Playfield = spawnReq.Playfield
                        };
                    }

                    // Получаем EntitySpawner из контейнера
                    if (_container == null || !_container.TryResolve<IEntitySpawner>(out var entitySpawner) || entitySpawner == null)
                    {
                        _logger?.Error("[PfServer] EntitySpawner not available");
                        return new SpawnStructureResponse
                        {
                            Success = false,
                            ErrorMessage = "EntitySpawner not initialized",
                            Playfield = playfieldName
                        };
                    }

                    // Выполняем spawn
                    try
                    {
                        var position = new Models.Vector3(spawnReq.Position[0], spawnReq.Position[1], spawnReq.Position[2]);
                        var rotation = new Models.Vector3(spawnReq.Rotation[0], spawnReq.Rotation[1], spawnReq.Rotation[2]);

                        _logger?.Info($"[PfServer] Spawning structure {spawnReq.PrefabName} at {position}");
                        
                        var entityId = await entitySpawner.SpawnStructureAsync(
                            spawnReq.Playfield,
                            spawnReq.PrefabName,
                            position,
                            rotation,
                            spawnReq.FactionId
                        );

                        _logger?.Info($"[PfServer] ✅ Structure spawn successful: EntityId={entityId}");

                        return new SpawnStructureResponse
                        {
                            Success = true,
                            EntityId = entityId,
                            Playfield = spawnReq.Playfield
                        };
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, $"[PfServer] ❌ Structure spawn failed: {spawnReq.PrefabName}");
                        return new SpawnStructureResponse
                        {
                            Success = false,
                            ErrorMessage = ex.Message,
                            Playfield = spawnReq.Playfield
                        };
                    }
                }
                else if (request is SpawnNPCRequest npcReq)
                {
                    // КРИТИЧНО: Жесткая проверка playfield для NPC!
                    if (npcReq.Playfield != _currentPlayfield)
                    {
                        var error = $"Playfield mismatch! Requested={npcReq.Playfield}, Current={_currentPlayfield}. Cannot spawn NPC in wrong playfield!";
                        _logger?.Error($"[PfServer] ❌ {error}");
                        return new SpawnNPCResponse
                        {
                            Success = false,
                            ErrorMessage = error,
                            Playfield = npcReq.Playfield
                        };
                    }

                    // Получаем EntitySpawner
                    if (_container == null || !_container.TryResolve<IEntitySpawner>(out var entitySpawner) || entitySpawner == null)
                    {
                        _logger?.Error("[PfServer] EntitySpawner not available for NPC spawn");
                        return new SpawnNPCResponse
                        {
                            Success = false,
                            ErrorMessage = "EntitySpawner not initialized",
                            Playfield = playfieldName
                        };
                    }

                    // Выполняем NPC spawn (terrain height определится в EntitySpawner)
                    try
                    {
                        float x = npcReq.Position[0];
                        float z = npcReq.Position[2];

                        _logger?.Info($"[PfServer] Spawning NPC {npcReq.NPCClassName} at ({x}, {z})");
                        
                        var entityId = await entitySpawner.SpawnNPCAtTerrainAsync(
                            npcReq.Playfield,
                            npcReq.NPCClassName,
                            x,
                            z,
                            npcReq.FactionName
                        );

                        _logger?.Info($"[PfServer] ✅ NPC spawn successful: EntityId={entityId}");

                        return new SpawnNPCResponse
                        {
                            Success = true,
                            EntityId = entityId,
                            Playfield = npcReq.Playfield
                        };
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, $"[PfServer] ❌ NPC spawn failed: {npcReq.NPCClassName}");
                        return new SpawnNPCResponse
                        {
                            Success = false,
                            ErrorMessage = ex.Message,
                            Playfield = npcReq.Playfield
                        };
                    }
                }
                else if (request is PlayfieldReadyRequest readyReq)
                {
                    // Проверка готовности playfield
                    bool isReady = _gateway != null && _currentPlayfield == readyReq.Playfield;
                    
                    _logger?.Info($"[PfServer] Playfield ready check: {isReady}");
                    
                    return new PlayfieldReadyResponse
                    {
                        IsReady = isReady,
                        Playfield = readyReq.Playfield,
                        Info = isReady ? "Playfield loaded and ready" : "Playfield not loaded or name mismatch"
                    };
                }

                _logger?.Warn($"[PfServer] Unknown request type: {request.MessageType}");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[PfServer] Error handling IPC request");
                return null;
            }
        }

        /// <summary>
        /// Обработчик IPC запросов с использованием NativePlayfieldSpawner (НОВАЯ АРХИТЕКТУРА).
        /// Вызывается в PfServer процессе для обработки команд спавна от Dedi через прямой IPlayfield API.
        /// КРИТИЧНО: Жесткая проверка playfield перед выполнением операций!
        /// </summary>
        private async Task<IPCMessage?> HandleIPCRequestWithNativeSpawner(
            IPCMessage request, 
            string playfieldName, 
            INativePlayfieldSpawner spawner)
        {
            try
            {
                _logger?.Info($"[PfServer-Native] Handling IPC request: {request.MessageType} (playfield: {playfieldName})");

                if (request is SpawnStructureRequest spawnReq)
                {
                    // КРИТИЧНО: Жесткая проверка playfield!
                    // Если не совпадает - возвращаем ошибку, НЕ продолжаем!
                    if (spawnReq.Playfield != _currentPlayfield)
                    {
                        var error = $"Playfield mismatch! Requested={spawnReq.Playfield}, Current={_currentPlayfield}. Cannot spawn in wrong playfield!";
                        _logger?.Error($"[PfServer-Native] ❌ {error}");
                        return new SpawnStructureResponse
                        {
                            Success = false,
                            ErrorMessage = error,
                            Playfield = spawnReq.Playfield
                        };
                    }

                    try
                    {
                        // X и Z приходят с Dedi. Y с Dedi — запасная высота без рельефа, её нельзя использовать,
                        // если запрос просит поставить структуру на землю (Module_04 §3.2, Module_06 §4).
                        float x = spawnReq.Position[0];
                        float y = spawnReq.Position[1];
                        float z = spawnReq.Position[2];

                        if (spawnReq.SnapToTerrain)
                        {
                            float terrainHeight;
                            try
                            {
                                terrainHeight = spawner.GetTerrainHeight(x, z);
                            }
                            catch (Exception terrainEx)
                            {
                                var terrainError = $"Terrain height unavailable at ({x}, {z}): {terrainEx.Message}";
                                _logger?.Error(terrainEx, $"[PfServer-Native] {terrainError}. Spawn of '{spawnReq.PrefabName}' aborted.");
                                return new SpawnStructureResponse
                                {
                                    Success = false,
                                    ErrorMessage = terrainError,
                                    Playfield = spawnReq.Playfield
                                };
                            }

                            y = TerrainSpawnHeight.Resolve(terrainHeight, spawnReq.HeightOffset);
                            _logger?.Info($"[PfServer-Native] Snapped '{spawnReq.PrefabName}' to terrain Y={y:F1} (terrain={terrainHeight:F1} + offset={spawnReq.HeightOffset:F1}) at ({x:F1}, {z:F1})");
                        }

                        var position = new UnityEngine.Vector3(x, y, z);
                        var rotation = UnityTypeConverter.ToUnityQuaternion(
                            new Models.Vector3(spawnReq.Rotation[0], spawnReq.Rotation[1], spawnReq.Rotation[2])
                        );

                        _logger?.Info($"[PfServer-Native] Spawning structure {spawnReq.PrefabName} at {position}");
                        
                        // ПРЯМОЙ СПАВН через IPlayfield.SpawnPrefab()!
                        var entityId = await spawner.SpawnStructureAsync(spawnReq.PrefabName, position, rotation);

                        _logger?.Info($"[PfServer-Native] ✅ Structure spawn successful: EntityId={entityId}");

                        return new SpawnStructureResponse
                        {
                            Success = true,
                            EntityId = entityId,
                            Playfield = spawnReq.Playfield
                        };
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, $"[PfServer-Native] ❌ Structure spawn failed: {spawnReq.PrefabName}");
                        return new SpawnStructureResponse
                        {
                            Success = false,
                            ErrorMessage = ex.Message,
                            Playfield = spawnReq.Playfield
                        };
                    }
                }
                else if (request is SpawnNPCRequest npcReq)
                {
                    // КРИТИЧНО: Жесткая проверка playfield для NPC!
                    if (npcReq.Playfield != _currentPlayfield)
                    {
                        var error = $"Playfield mismatch! Requested={npcReq.Playfield}, Current={_currentPlayfield}. Cannot spawn NPC in wrong playfield!";
                        _logger?.Error($"[PfServer-Native] ❌ {error}");
                        return new SpawnNPCResponse
                        {
                            Success = false,
                            ErrorMessage = error,
                            Playfield = npcReq.Playfield
                        };
                    }

                    try
                    {
                        // Для NPC используем terrain height из NativePlayfieldSpawner
                        float x = npcReq.Position[0];
                        float z = npcReq.Position[2];
                        
                        // Получаем высоту рельефа через прямой вызов IPlayfield.GetTerrainHeightAt()
                        float terrainHeight = spawner.GetTerrainHeight(x, z);
                        float y = terrainHeight + 0.5f; // Offset над землей

                        var position = new UnityEngine.Vector3(x, y, z);
                        var rotation = UnityTypeConverter.ToUnityQuaternion(
                            new Models.Vector3(npcReq.Rotation[0], npcReq.Rotation[1], npcReq.Rotation[2])
                        );

                        _logger?.Info($"[PfServer-Native] Spawning NPC {npcReq.NPCClassName} at ({x}, {y}, {z})");
                        
                        // ПРЯМОЙ СПАВН через IPlayfield.SpawnEntity()!
                        var entityId = await spawner.SpawnNPCAsync(npcReq.NPCClassName, position, rotation);

                        _logger?.Info($"[PfServer-Native] ✅ NPC spawn successful: EntityId={entityId}");

                        return new SpawnNPCResponse
                        {
                            Success = true,
                            EntityId = entityId,
                            Playfield = npcReq.Playfield
                        };
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, $"[PfServer-Native] ❌ NPC spawn failed: {npcReq.NPCClassName}");
                        return new SpawnNPCResponse
                        {
                            Success = false,
                            ErrorMessage = ex.Message,
                            Playfield = npcReq.Playfield
                        };
                    }
                }
                else if (request is EntityExistsRequest existsReq)
                {
                    // Чужой playfield не опрашиваем: сущности этого процесса ему не принадлежат.
                    if (existsReq.Playfield != _currentPlayfield)
                    {
                        var error = $"Playfield mismatch! Requested={existsReq.Playfield}, Current={_currentPlayfield}. Cannot check entity on the wrong playfield!";
                        _logger?.Error($"[PfServer-Native] {error}");
                        return new EntityExistsResponse
                        {
                            Success = false,
                            Exists = false,
                            ErrorMessage = error,
                            Playfield = existsReq.Playfield,
                            EntityId = existsReq.EntityId
                        };
                    }

                    try
                    {
                        var exists = spawner.EntityExists(existsReq.EntityId);
                        _logger?.Info($"[PfServer-Native] Entity {existsReq.EntityId} exists={exists}");
                        return new EntityExistsResponse
                        {
                            Success = true,
                            Exists = exists,
                            Playfield = existsReq.Playfield,
                            EntityId = existsReq.EntityId
                        };
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, $"[PfServer-Native] Entity exists check failed for {existsReq.EntityId}");
                        return new EntityExistsResponse
                        {
                            Success = false,
                            Exists = false,
                            ErrorMessage = ex.Message,
                            Playfield = existsReq.Playfield,
                            EntityId = existsReq.EntityId
                        };
                    }
                }
                else if (request is DestroyEntityRequest destroyReq)
                {
                    if (destroyReq.Playfield != _currentPlayfield)
                    {
                        var error = $"Playfield mismatch! Requested={destroyReq.Playfield}, Current={_currentPlayfield}. Cannot destroy entity on the wrong playfield!";
                        _logger?.Error($"[PfServer-Native] {error}");
                        return new DestroyEntityResponse
                        {
                            Success = false,
                            ErrorMessage = error,
                            Playfield = destroyReq.Playfield,
                            EntityId = destroyReq.EntityId
                        };
                    }

                    try
                    {
                        _logger?.Info($"[PfServer-Native] Removing entity {destroyReq.EntityId}");
                        spawner.RemoveEntity(destroyReq.EntityId);
                        return new DestroyEntityResponse
                        {
                            Success = true,
                            Playfield = destroyReq.Playfield,
                            EntityId = destroyReq.EntityId
                        };
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, $"[PfServer-Native] Destroy entity failed for {destroyReq.EntityId}");
                        return new DestroyEntityResponse
                        {
                            Success = false,
                            ErrorMessage = ex.Message,
                            Playfield = destroyReq.Playfield,
                            EntityId = destroyReq.EntityId
                        };
                    }
                }
                else if (request is PlayfieldReadyRequest readyReq)
                {
                    // Проверка готовности playfield (теперь без зависимости от Gateway)
                    bool isReady = _currentPlayfield == readyReq.Playfield && spawner != null;
                    
                    _logger?.Info($"[PfServer-Native] Playfield ready check: {isReady}");
                    
                    return new PlayfieldReadyResponse
                    {
                        IsReady = isReady,
                        Playfield = readyReq.Playfield,
                        Info = isReady ? "Playfield loaded and ready (native spawner)" : "Playfield not loaded or name mismatch"
                    };
                }

                _logger?.Warn($"[PfServer-Native] Unknown request type: {request.MessageType}");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[PfServer-Native] Error handling IPC request");
                return null;
            }
        }

        /// <summary>
        /// Остановка мода (IMod.Shutdown).
        /// Вызывается Dedicated Server при остановке мода.
        /// </summary>
        public void Shutdown()
        {
            _logger?.Info("IMod.Shutdown called");
            
            // Очищаем NetworkBridge
            _networkBridge?.Dispose();
            
            // Используем тот же метод остановки что и Game_Exit
            Game_Exit();
        }

        #endregion
    }
}
