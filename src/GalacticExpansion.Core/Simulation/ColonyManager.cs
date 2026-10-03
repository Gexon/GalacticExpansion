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

                // 4. Защита структур от decay (только для материализованных колоний).
                // Касание уходит отдельной задачей: тик колонии не ждёт ответ игры.
                RequestStructureMaintenance(colony);
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
        /// Помечает виртуальные колонии на данном playfield для материализации.
        /// Вызывается из двух мест:
        /// 1. ColonyTickModule.OnGameEvent → Event_Playfield_Loaded (Empyrion API)
        /// 2. ModMain.OnPlayfieldReadyReceived → IPC PlayfieldReadyNotification от PfServer
        ///
        /// PfServer отправляет PlayfieldReadyNotification ПОСЛЕ полной инициализации NativePlayfieldSpawner,
        /// что гарантирует готовность playfield к spawn-операциям.
        /// Retry-логика в TryMaterializePendingColoniesAsync обеспечивает надёжность при IPC ошибках.
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
                    // Тот же часовой интервал, что и у тика. Ответ игры здесь тоже не ждём.
                    RequestStructureMaintenance(colony);
                }
            }
        }

        /// <summary>
        /// Пытается материализовать колонии, помеченные для материализации (retry-логика).
        /// 
        /// ВАЖНО: Этот метод запускается через Task.Run() из ModMain, а НЕ через
        /// .GetAwaiter().GetResult() — это критично для предотвращения deadlock на Unity main thread.
        /// Готовность playfield определяется через IPC PlayfieldReadyNotification от PfServer
        /// (fire-and-forget уведомление после инициализации NativePlayfieldSpawner),
        /// а не через Request_Playfield_Stats (который вызывал deadlock при синхронном вызове).
        /// 
        /// Работает с in-memory state — изменения видны сразу без сохранения в файл.
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
                    _logger.Warn($"Colony {colony.Id} failed to materialize after {maxAttempts} attempts. Giving up.");
                    colony.PendingMaterialization = false;
                    continue;
                }

                colony.MaterializationAttempts++;

                try
                {
                    _logger.Info($"Materializing colony {colony.Id} on '{colony.Playfield}' (attempt {colony.MaterializationAttempts}/{maxAttempts})");
                    await _stageManager.MaterializeColonyAsync(colony);
                    
                    colony.PendingMaterialization = false;
                    colony.MaterializationAttempts = 0;
                    
                    _logger.Info($"✅ Colony {colony.Id} materialized successfully on '{colony.Playfield}'");
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Materialization attempt {colony.MaterializationAttempts} failed for colony {colony.Id}: {ex.Message}");
                }
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
        /// Как часто сбрасывать таймер распада. Module_07 §6: раз в час, не раз в минуту.
        /// </summary>
        private static readonly TimeSpan StructureMaintenanceInterval = TimeSpan.FromHours(1);

        /// <summary>
        /// Запускает касание структур, если с прошлого раза прошёл час.
        /// Время попытки записывается сразу, чтобы сбой не повторял запрос на каждом тике.
        /// Сам запрос к игре не ждём: ответ приходит отдельно и не держит тик колонии.
        /// </summary>
        /// <param name="colony">Колония, чьи структуры нужно защитить от распада.</param>
        private void RequestStructureMaintenance(Colony colony)
        {
            if (colony.IsVirtual || !ShouldMaintainStructures(colony))
                return;

            colony.LastMaintenanceTime = DateTime.UtcNow;
            _ = MaintainStructuresWithoutBlockingTickAsync(colony);
        }

        /// <summary>
        /// Касается структур в фоне. Ошибка остаётся предупреждением и не помечает весь тик как сбойный.
        /// </summary>
        /// <param name="colony">Колония, для которой уже решено, что час прошёл.</param>
        private async Task MaintainStructuresWithoutBlockingTickAsync(Colony colony)
        {
            try
            {
                await _stageManager.MaintainColonyStructuresAsync(colony);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to maintain structures for colony {colony.Id}");
            }
        }

        /// <summary>
        /// Проверяет, пора ли снова сбрасывать таймер распада.
        /// Первый раз — сразу. Дальше не чаще одного раза в час.
        /// </summary>
        /// <param name="colony">Колония с полем времени прошлого касания.</param>
        /// <returns>true, если касание ещё не делали или с прошлого прошёл час.</returns>
        private static bool ShouldMaintainStructures(Colony colony)
        {
            if (!colony.LastMaintenanceTime.HasValue)
                return true;

            return DateTime.UtcNow - colony.LastMaintenanceTime.Value >= StructureMaintenanceInterval;
        }
    }
}
