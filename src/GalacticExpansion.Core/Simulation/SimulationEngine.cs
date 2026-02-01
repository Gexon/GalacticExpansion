using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GalacticExpansion.Core.Simulation.Events;
using GalacticExpansion.Core.State;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Реализация главного движка симуляции.
    /// Управляет таймером тиков, загрузкой/сохранением state, жизненным циклом модулей.
    /// </summary>
    public class SimulationEngine : ISimulationEngine
    {
        private readonly IStateStore _stateStore;
        private readonly IModuleRegistry _moduleRegistry;
        private readonly IEventBus _eventBus;
        private readonly ILogger _logger;

        private SimulationState _state;
        private Timer? _simulationTimer;
        private long _tickNumber;
        private DateTime _lastTickTime;
        private DateTime _lastSaveTime;
        private bool _isRunning;
        private readonly object _stateLock = new object();

        // Интервал тика симуляции (в миллисекундах)
        private const int TickIntervalMs = 1000;
        
        // Интервал автосохранения (в секундах)
        private const int AutoSaveIntervalSeconds = 60;

        /// <inheritdoc/>
        public SimulationState State 
        { 
            get 
            {
                lock (_stateLock)
                {
                    return _state;
                }
            }
        }

        /// <inheritdoc/>
        public bool IsRunning 
        { 
            get 
            {
                lock (_stateLock)
                {
                    return _isRunning;
                }
            }
        }

        /// <summary>
        /// Создает новый экземпляр SimulationEngine.
        /// </summary>
        /// <param name="stateStore">Хранилище состояния</param>
        /// <param name="moduleRegistry">Реестр модулей</param>
        /// <param name="eventBus">Шина событий</param>
        /// <param name="logger">Логгер</param>
        public SimulationEngine(
            IStateStore stateStore,
            IModuleRegistry moduleRegistry,
            IEventBus eventBus,
            ILogger logger)
        {
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            _moduleRegistry = moduleRegistry ?? throw new ArgumentNullException(nameof(moduleRegistry));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _state = new SimulationState();
            _tickNumber = 0;
            _lastTickTime = DateTime.UtcNow;
            _lastSaveTime = DateTime.UtcNow;
            _isRunning = false;

            _logger.Debug("SimulationEngine created");
        }

        /// <inheritdoc/>
        public void RegisterModule(ISimulationModule module)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));

            if (_isRunning)
                throw new InvalidOperationException("Cannot register modules while simulation is running");

            _moduleRegistry.RegisterModule(module);
        }

        /// <inheritdoc/>
        public async Task StartAsync()
        {
            lock (_stateLock)
            {
                if (_isRunning)
                {
                    _logger.Warn("SimulationEngine is already running");
                    return;
                }
            }

            _logger.Info("SimulationEngine starting...");

            try
            {
                // 1. Загрузка state
                _logger.Info("Loading simulation state...");
                _state = await _stateStore.LoadAsync();
                _logger.Info($"State loaded: {_state.Colonies.Count} colonies, version {_state.Version}");

                // 2. Инициализация модулей
                _logger.Info("Initializing modules...");
                await _moduleRegistry.InitializeAllModulesAsync(_state);

                // 3. Запуск таймера тиков
                _logger.Info($"Starting simulation timer (interval={TickIntervalMs}ms)");
                _simulationTimer = new Timer(
                    OnSimulationTick,
                    null,
                    TimeSpan.FromMilliseconds(TickIntervalMs),
                    TimeSpan.FromMilliseconds(TickIntervalMs)
                );

                lock (_stateLock)
                {
                    _isRunning = true;
                }

                _lastTickTime = DateTime.UtcNow;
                _lastSaveTime = DateTime.UtcNow;
                _tickNumber = 0;

                // 4. Публикация события старта
                _eventBus.Publish(new SimulationStartedEvent
                {
                    StartTime = DateTime.UtcNow,
                    ModuleCount = _moduleRegistry.ModuleCount
                });

                _logger.Info("SimulationEngine started successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to start SimulationEngine: {ex.Message}");
                lock (_stateLock)
                {
                    _isRunning = false;
                }
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task StopAsync()
        {
            lock (_stateLock)
            {
                if (!_isRunning)
                {
                    _logger.Warn("SimulationEngine is not running");
                    return;
                }
            }

            _logger.Info("SimulationEngine stopping...");

            try
            {
                // 1. Остановка таймера
                if (_simulationTimer != null)
                {
                    _logger.Debug("Stopping simulation timer...");
                    _simulationTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    _simulationTimer.Dispose();
                    _simulationTimer = null;
                }

                lock (_stateLock)
                {
                    _isRunning = false;
                }

                // 2. Сохранение state
                _logger.Info("Saving simulation state...");
                await _stateStore.SaveAsync(_state);

                // 3. Завершение модулей
                _logger.Info("Shutting down modules...");
                await _moduleRegistry.ShutdownAllModulesAsync();

                _logger.Info("SimulationEngine stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error stopping SimulationEngine: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Обработчик тика симуляции.
        /// Вызывается таймером каждую секунду.
        /// </summary>
        private void OnSimulationTick(object? state)
        {
            if (!_isRunning)
                return;

            var sw = Stopwatch.StartNew();
            _tickNumber++;

            try
            {
                // Вычисляем deltaTime
                var currentTime = DateTime.UtcNow;
                var deltaTime = (float)(currentTime - _lastTickTime).TotalSeconds;
                _lastTickTime = currentTime;

                // Создаем контекст тика
                var context = new SimulationContext
                {
                    CurrentState = _state,
                    DeltaTime = deltaTime,
                    CurrentTime = currentTime,
                    TickNumber = _tickNumber
                };

                _logger.Trace($"Simulation tick #{_tickNumber} (dt={deltaTime:F2}s)");

                // Обновляем все модули
                _moduleRegistry.UpdateAllModules(context);

                // Публикуем событие тика
                sw.Stop();
                _eventBus.Publish(new SimulationTickEvent
                {
                    TickNumber = _tickNumber,
                    TickDurationMs = sw.ElapsedMilliseconds,
                    TickTime = currentTime
                });

                _logger.Trace($"Simulation tick #{_tickNumber} completed in {sw.ElapsedMilliseconds}ms");

                // Периодическое автосохранение
                if ((currentTime - _lastSaveTime).TotalSeconds >= AutoSaveIntervalSeconds)
                {
                    if (_state.IsDirty)
                    {
                        _logger.Debug("Auto-saving state...");
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _stateStore.SaveAsync(_state);
                                _state.IsDirty = false;
                                _logger.Debug("Auto-save completed");
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, $"Auto-save failed: {ex.Message}");
                            }
                        });
                    }
                    _lastSaveTime = currentTime;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in simulation tick #{_tickNumber}: {ex.Message}");
            }
        }
    }
}
