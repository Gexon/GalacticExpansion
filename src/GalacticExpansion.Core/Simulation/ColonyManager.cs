using System;
using System.Linq;
using System.Threading.Tasks;
using GalacticExpansion.Core.Economy;
using GalacticExpansion.Core.Spawning;
using GalacticExpansion.Core.State;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Реализация менеджера колоний.
    /// Координирует все аспекты управления колонией для упрощения Core Loop.
    /// </summary>
    public class ColonyManager : IColonyManager
    {
        private readonly IStageManager _stageManager;
        private readonly IEconomySimulator _economySimulator;
        private readonly IUnitEconomyManager _unitEconomy;
        private readonly IStateStore _stateStore;
        private readonly ILogger _logger;

        /// <summary>
        /// Создаёт менеджер колоний с зависимостями: стадии, экономика, юнит-экономика, хранилище, логгер.
        /// </summary>
        /// <param name="stageManager">Менеджер стадий колоний.</param>
        /// <param name="economySimulator">Симулятор экономики.</param>
        /// <param name="unitEconomy">Менеджер юнит-экономики.</param>
        /// <param name="stateStore">Хранилище состояния.</param>
        /// <param name="logger">Логгер.</param>
        public ColonyManager(
            IStageManager stageManager,
            IEconomySimulator economySimulator,
            IUnitEconomyManager unitEconomy,
            IStateStore stateStore,
            ILogger logger)
        {
            _stageManager = stageManager ?? throw new ArgumentNullException(nameof(stageManager));
            _economySimulator = economySimulator ?? throw new ArgumentNullException(nameof(economySimulator));
            _unitEconomy = unitEconomy ?? throw new ArgumentNullException(nameof(unitEconomy));
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            // Добавление в state
            var state = await _stateStore.LoadAsync();
            state.Colonies.Add(colony);
            await _stateStore.SaveAsync(state);

            return colony;
        }

        /// <summary>
        /// Удаляет колонию
        /// </summary>
        /// <summary>
        /// При загрузке playfield вызывается из ColonyTickModule по Event_Playfield_Loaded.
        /// Помечает виртуальные колонии для материализации (отложенный спавн через retry-логику).
        /// </summary>
        public async Task EnsurePlayfieldColoniesSpawnedAsync(string playfield)
        {
            if (string.IsNullOrWhiteSpace(playfield))
                return;

            var state = await _stateStore.LoadAsync();
            var coloniesOnPlayfield = state.Colonies
                .Where(c => string.Equals(c.Playfield, playfield, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            if (coloniesOnPlayfield.Count == 0)
                return;

            _logger.Info($"EnsurePlayfieldColoniesSpawned: playfield '{playfield}', {coloniesOnPlayfield.Count} colony(ies)");
            
            foreach (var colony in coloniesOnPlayfield)
            {
                // Если колония виртуальная - помечаем для материализации (отложенный спавн)
                if (colony.IsVirtual && !colony.PendingMaterialization)
                {
                    colony.PendingMaterialization = true;
                    colony.MaterializationAttempts = 0;
                    _logger.Info($"Virtual colony {colony.Id} marked for materialization (will retry in next ticks)");
                }
                else if (!colony.IsVirtual)
                {
                    // Если уже материализована - защищаем структуры
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
            
            // Сохраняем изменения (PendingMaterialization флаги обновлены)
            await _stateStore.SaveAsync(state);
        }

        /// <summary>
        /// Пытается материализовать колонии, помеченные для материализации (retry-логика).
        /// Вызывается каждый тик из ColonyTickModule.
        /// </summary>
        public async Task TryMaterializePendingColoniesAsync()
        {
            var state = await _stateStore.LoadAsync();
            var pendingColonies = state.Colonies
                .Where(c => c.IsVirtual && c.PendingMaterialization)
                .ToList();

            if (pendingColonies.Count == 0)
                return;

            _logger.Debug($"TryMaterializePendingColonies: {pendingColonies.Count} colony(ies) pending materialization");

            bool stateChanged = false;

            foreach (var colony in pendingColonies)
            {
                const int maxAttempts = 10; // Максимум 10 попыток (10 секунд)

                if (colony.MaterializationAttempts >= maxAttempts)
                {
                    _logger.Warn($"Colony {colony.Id} failed to materialize after {maxAttempts} attempts. Giving up.");
                    colony.PendingMaterialization = false;
                    stateChanged = true;
                    continue;
                }

                colony.MaterializationAttempts++;

                try
                {
                    _logger.Debug($"Attempting to materialize colony {colony.Id} (attempt {colony.MaterializationAttempts}/{maxAttempts})");
                    await _stageManager.MaterializeColonyAsync(colony);
                    
                    // Успех!
                    colony.PendingMaterialization = false;
                    colony.MaterializationAttempts = 0;
                    stateChanged = true;
                    
                    _logger.Info($"✅ Colony {colony.Id} materialized successfully on attempt {colony.MaterializationAttempts}");
                }
                catch (Exception ex)
                {
                    // Ошибка - попробуем в следующий тик
                    _logger.Debug($"Materialization attempt {colony.MaterializationAttempts} failed for colony {colony.Id}: {ex.Message}");
                    stateChanged = true; // Обновляем счетчик попыток
                }
            }

            if (stateChanged)
            {
                await _stateStore.SaveAsync(state);
            }
        }

        /// <summary>
        /// Удаляет колонию из системы
        /// </summary>
        public async Task RemoveColonyAsync(string colonyId)
        {
            var state = await _stateStore.LoadAsync();
            var colony = state.Colonies.FirstOrDefault(c => c.Id == colonyId);

            if (colony != null)
            {
                // Удаляем колонию из списка
                state.Colonies.Remove(colony);
                
                // Сохраняем измененный state (не загружаем заново!)
                await _stateStore.SaveAsync(state);

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
