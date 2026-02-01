using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Реализация реестра модулей с изоляцией ошибок и мониторингом производительности.
    /// </summary>
    public class ModuleRegistry : IModuleRegistry
    {
        private readonly ILogger _logger;
        private readonly List<ISimulationModule> _modules;
        private readonly object _modulesLock = new object();
        
        // Порог предупреждения о долгом выполнении модуля (в миллисекундах)
        private const int SlowModuleThresholdMs = 100;

        /// <summary>
        /// Создает новый реестр модулей.
        /// </summary>
        /// <param name="logger">Логгер для мониторинга</param>
        public ModuleRegistry(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _modules = new List<ISimulationModule>();
            
            _logger.Debug("ModuleRegistry initialized");
        }

        /// <inheritdoc/>
        public int ModuleCount
        {
            get
            {
                lock (_modulesLock)
                {
                    return _modules.Count;
                }
            }
        }

        /// <inheritdoc/>
        public void RegisterModule(ISimulationModule module)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));

            lock (_modulesLock)
            {
                // Проверяем, что модуль с таким именем еще не зарегистрирован
                if (_modules.Any(m => m.ModuleName == module.ModuleName))
                {
                    _logger.Warn($"Module '{module.ModuleName}' is already registered, skipping");
                    return;
                }

                _modules.Add(module);
                
                // Сортируем модули по приоритету (меньше = раньше)
                _modules.Sort((a, b) => a.UpdatePriority.CompareTo(b.UpdatePriority));
                
                _logger.Info($"Registered module: {module.ModuleName} (Priority={module.UpdatePriority})");
            }
        }

        /// <inheritdoc/>
        public async Task InitializeAllModulesAsync(SimulationState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            List<ISimulationModule> modulesCopy;
            lock (_modulesLock)
            {
                modulesCopy = new List<ISimulationModule>(_modules);
            }

            _logger.Info($"Initializing {modulesCopy.Count} modules...");

            foreach (var module in modulesCopy)
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    await module.InitializeAsync(state);
                    sw.Stop();
                    
                    _logger.Info($"Module '{module.ModuleName}' initialized in {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    // Изолируем ошибки инициализации - один упавший модуль не должен ломать остальные
                    _logger.Error(ex, $"Failed to initialize module '{module.ModuleName}': {ex.Message}");
                }
            }
        }

        /// <inheritdoc/>
        public void UpdateAllModules(SimulationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            List<ISimulationModule> modulesCopy;
            lock (_modulesLock)
            {
                modulesCopy = new List<ISimulationModule>(_modules);
            }

            // Обновляем модули в порядке приоритета
            foreach (var module in modulesCopy)
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    module.OnSimulationUpdate(context);
                    sw.Stop();
                    
                    // Предупреждаем о медленных модулях
                    if (sw.ElapsedMilliseconds > SlowModuleThresholdMs)
                    {
                        _logger.Warn($"Module '{module.ModuleName}' took {sw.ElapsedMilliseconds}ms to update (threshold: {SlowModuleThresholdMs}ms)");
                    }
                    else
                    {
                        _logger.Trace($"Module '{module.ModuleName}' updated in {sw.ElapsedMilliseconds}ms");
                    }
                }
                catch (Exception ex)
                {
                    // Изолируем ошибки обновления - один упавший модуль не должен ломать остальные
                    _logger.Error(ex, $"Error updating module '{module.ModuleName}': {ex.Message}");
                }
            }
        }

        /// <inheritdoc/>
        public async Task ShutdownAllModulesAsync()
        {
            List<ISimulationModule> modulesCopy;
            lock (_modulesLock)
            {
                modulesCopy = new List<ISimulationModule>(_modules);
            }

            _logger.Info($"Shutting down {modulesCopy.Count} modules...");

            // Завершаем модули в обратном порядке приоритета
            modulesCopy.Reverse();

            foreach (var module in modulesCopy)
            {
                try
                {
                    await module.ShutdownAsync();
                    _logger.Info($"Module '{module.ModuleName}' shut down successfully");
                }
                catch (Exception ex)
                {
                    // Логируем ошибки, но продолжаем завершение остальных модулей
                    _logger.Error(ex, $"Error shutting down module '{module.ModuleName}': {ex.Message}");
                }
            }
        }
    }
}
