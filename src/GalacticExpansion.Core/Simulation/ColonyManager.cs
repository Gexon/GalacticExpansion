using System;
using System.Linq;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Economy;
using GalacticExpansion.Core.Gateway;
using GalacticExpansion.Core.Spawning;
using GalacticExpansion.Core.State;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Реализация менеджера колоний.
    /// Координирует все аспекты управления колонией для упрощения Core Loop.
    /// 
    /// ВАЖНО: после старта SimulationEngine вызывается SetSimulationState(),
    /// и ColonyManager работает с in-memory state напрямую (без чтения файла).
    /// _stateStore используется только при начальной инициализации (CreateColonyAsync до старта симуляции).
    /// </summary>
    public class ColonyManager : IColonyManager
    {
        private readonly IEmpyrionGateway _gateway;
        private readonly IStageManager _stageManager;
        private readonly IEconomySimulator _economySimulator;
        private readonly IUnitEconomyManager _unitEconomy;
        private readonly IStateStore _stateStore;
        private readonly ILogger _logger;

        // Ссылка на in-memory SimulationState из SimulationEngine.
        // До вызова SetSimulationState() == null, и методы используют _stateStore как fallback.
        private SimulationState? _simulationState;

        /// <summary>
        /// Создаёт менеджер колоний с зависимостями: gateway, стадии, экономика, юнит-экономика, хранилище, логгер.
        /// </summary>
        public ColonyManager(
            IEmpyrionGateway gateway,
            IStageManager stageManager,
            IEconomySimulator economySimulator,
            IUnitEconomyManager unitEconomy,
            IStateStore stateStore,
            ILogger logger)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _stageManager = stageManager ?? throw new ArgumentNullException(nameof(stageManager));
            _economySimulator = economySimulator ?? throw new ArgumentNullException(nameof(economySimulator));
            _unitEconomy = unitEconomy ?? throw new ArgumentNullException(nameof(unitEconomy));
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public void SetSimulationState(SimulationState state)
        {
            _simulationState = state ?? throw new ArgumentNullException(nameof(state));
            _logger.Info($"ColonyManager: SimulationState injected ({state.Colonies.Count} colonies in-memory)");
        }

        /// <summary>
        /// Возвращает in-memory state (если доступен) или загружает из файла (fallback при инициализации).
        /// </summary>
        private async Task<SimulationState> GetStateAsync()
        {
            if (_simulationState != null)
                return _simulationState;

            return await _stateStore.LoadAsync();
        }

        /// <summary>
        /// Обновляет колонию: экономику, производство юнитов, проверку апгрейдов, защиту структур.
        /// Виртуальные колонии также обновляются, но без физических операций со структурами.
        /// </summary>
        public async Task UpdateColonyAsync(Colony colony, float deltaTime)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));

            var virtualFlag = colony.IsVirtual ? " [VIRTUAL]" : "";
            _logger.Debug($"ColonyManager: updating colony {colony.Id} ({colony.Playfield}, stage={colony.Stage}, dt={deltaTime:F2}s){virtualFlag}");

            try
            {
                // 1. Обновление производства ресурсов (работает для виртуальных)
                _economySimulator.UpdateProduction(colony, deltaTime);

                // 2. Обновление производства юнитов (работает для виртуальных)
                _unitEconomy.ProduceUnits(colony, deltaTime);

                // 3. Проверка возможности апгрейда (работает для виртуальных)
                if (await _stageManager.CanTransitionToNextStageAsync(colony))
                {
                    await _stageManager.TransitionToNextStageAsync(colony);
                }

                // 4. Защита структур от decay (ТОЛЬКО для материализованных колоний)
                if (!colony.IsVirtual && ShouldMaintainStructures(colony))
                {
                    await _stageManager.MaintainColonyStructuresAsync(colony);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error updating colony {colony.Id}");
            }
        }

        /// <summary>
        /// Создает новую колонию (может быть виртуальной)
        /// </summary>
        /// <param name="playfield">Название playfield</param>
        /// <param name="position">Позиция (может быть нулевой для виртуальных)</param>
        /// <param name="factionId">ID фракции</param>
        /// <param name="isVirtual">Создать виртуальную колонию (без спавна структур)</param>
        public async Task<Colony> CreateColonyAsync(string playfield, Vector3 position, int factionId, bool isVirtual = false)
        {
            var virtualFlag = isVirtual ? " [VIRTUAL]" : "";
            _logger.Info($"Creating new colony on '{playfield}' at {position}{virtualFlag}");

            var colony = await _stageManager.InitializeColonyAsync(playfield, position, factionId, isVirtual);

            // Добавление колонии в state (in-memory или файл при инициализации)
            var state = await GetStateAsync();
            state.Colonies.Add(colony);

            // При начальной инициализации (_simulationState == null) сохраняем в файл,
            // чтобы SimulationEngine.StartAsync() мог загрузить state с колонией.
            // После старта симуляции сохранение через автосохранение SimulationEngine.
            if (_simulationState == null)
            {
                await _stateStore.SaveAsync(state);
            }

            return colony;
        }

        /// <summary>
        /// Удаляет колонию
        /// </summary>
        /// <summary>
        /// При загрузке playfield вызывается из ColonyTickModule по Event_Playfield_Loaded.
        /// Помечает виртуальные колонии для материализации (отложенный спавн через retry-логику с задержкой).
        /// 
        /// Задержка нужна т.к. Event_Playfield_Loaded срабатывает когда playfield начинает загружаться,
        /// но ещё не готов для операций спавна через ModAPI (PlayfieldConnectionNotFound).
        /// 
        /// AI-контекст: Этот метод — критическая точка для материализации виртуальных колоний.
        /// Задержка в 3 секунды (через отрицательное значение MaterializationAttempts) даёт playfield
        /// время полностью загрузиться. Retry-логика обеспечивает надёжность при edge-cases.
        /// </summary>
        public async Task EnsurePlayfieldColoniesSpawnedAsync(string playfield)
        {
            if (string.IsNullOrWhiteSpace(playfield))
                return;

            // Работаем с in-memory state -- изменения PendingMaterialization будут видны в Game_Update
            var state = await GetStateAsync();
            var coloniesOnPlayfield = state.Colonies
                .Where(c => string.Equals(c.Playfield, playfield, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            if (coloniesOnPlayfield.Count == 0)
                return;

            _logger.Info($"EnsurePlayfieldColoniesSpawned: playfield '{playfield}', {coloniesOnPlayfield.Count} colony(ies)");
            
            foreach (var colony in coloniesOnPlayfield)
            {
                if (colony.IsVirtual && !colony.PendingMaterialization)
                {
                    colony.PendingMaterialization = true;
                    colony.MaterializationAttempts = 0;
                    _logger.Info($"Virtual colony {colony.Id} marked for materialization (playfield readiness will be checked)");
                }
                else if (!colony.IsVirtual)
                {
                    try
                    {
                        await _stageManager.MaintainColonyStructuresAsync(colony);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Error maintaining colony {colony.Id} structures");
                    }
                }
            }
        }

        /// <summary>
        /// Пытается материализовать колонии, помеченные для материализации (retry-логика с проверкой готовности).
        /// Вызывается из Game_Update() в Dedi процессе.
        /// 
        /// Работает с in-memory state -- изменения видны сразу без сохранения в файл.
        /// Персистентность обеспечивается автосохранением SimulationEngine каждые 60 сек.
        /// </summary>
        public async Task TryMaterializePendingColoniesAsync()
        {
            var state = await GetStateAsync();
            var pendingColonies = state.Colonies
                .Where(c => c.IsVirtual && c.PendingMaterialization)
                .ToList();

            if (pendingColonies.Count == 0)
                return;

            _logger.Debug($"TryMaterializePendingColonies: {pendingColonies.Count} colony(ies) pending materialization");

            foreach (var colony in pendingColonies)
            {
                const int maxAttempts = 30;

                if (colony.MaterializationAttempts >= maxAttempts)
                {
                    _logger.Warn($"Colony {colony.Id} failed to materialize after {maxAttempts} attempts (~90 seconds). Giving up.");
                    colony.PendingMaterialization = false;
                    continue;
                }

                colony.MaterializationAttempts++;

                _logger.Debug($"Checking playfield '{colony.Playfield}' readiness for colony {colony.Id} (attempt {colony.MaterializationAttempts}/{maxAttempts})");
                
                bool isReady = await IsPlayfieldReadyAsync(colony.Playfield);
                
                if (!isReady)
                {
                    _logger.Debug($"Playfield '{colony.Playfield}' not ready yet for colony {colony.Id}");
                    continue;
                }

                try
                {
                    _logger.Info($"Playfield '{colony.Playfield}' is READY. Materializing colony {colony.Id} (attempt {colony.MaterializationAttempts}/{maxAttempts})");
                    await _stageManager.MaterializeColonyAsync(colony);
                    
                    colony.PendingMaterialization = false;
                    colony.MaterializationAttempts = 0;
                    
                    _logger.Info($"✅ Colony {colony.Id} materialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Materialization attempt {colony.MaterializationAttempts} failed for colony {colony.Id}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Проверяет, готов ли плейфилд для операций спавна через Request_Playfield_Stats.
        /// 
        /// AI-контекст: Request_Playfield_Stats работает ТОЛЬКО когда playfield полностью загружен.
        /// Это надёжный способ проверки готовности плейфилда, рекомендованный в документации Empyrion.
        /// Ошибка означает "плейфилд ещё не готов" или "нет доступа к playfield connection".
        /// 
        /// ВАЖНО: Этот метод ДОЛЖЕН вызываться из Game_Update() в PfServer процессе!
        /// </summary>
        /// <param name="playfieldName">Имя плейфилда для проверки.</param>
        /// <returns>True если плейфилд готов, false если ещё загружается или нет доступа.</returns>
        private async Task<bool> IsPlayfieldReadyAsync(string playfieldName)
        {
            try 
            {
                // Запрашиваем статистику плейфилда (работает только когда он полностью загружен)
                // Используем короткий таймаут (2 секунды) чтобы не блокировать систему
                await _gateway.SendRequestAsync<PlayfieldStats>(
                    CmdId.Request_Playfield_Stats, 
                    new PString(playfieldName),
                    timeoutMs: 2000
                );
                
                _logger.Debug($"Playfield '{playfieldName}' is ready (Request_Playfield_Stats succeeded)");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Playfield '{playfieldName}' not ready yet: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Удаляет колонию из in-memory state.
        /// Персистентность обеспечивается автосохранением SimulationEngine.
        /// </summary>
        public async Task RemoveColonyAsync(string colonyId)
        {
            var state = await GetStateAsync();
            var colony = state.Colonies.FirstOrDefault(c => c.Id == colonyId);

            if (colony != null)
            {
                state.Colonies.Remove(colony);
                _logger.Info($"Colony {colonyId} removed from state");
            }
            else
            {
                _logger.Warn($"Colony {colonyId} not found in state");
            }
        }

        /// <summary>
        /// Проверяет, нужно ли защищать структуры (каждый час)
        /// </summary>
        private bool ShouldMaintainStructures(Colony colony)
        {
            // Простая проверка: каждый 60-й тик (при 1 тик/сек = каждую минуту для теста)
            // В production это должно быть настроено на 1 час
            return DateTime.UtcNow.Second % 60 == 0;
        }
    }
}
