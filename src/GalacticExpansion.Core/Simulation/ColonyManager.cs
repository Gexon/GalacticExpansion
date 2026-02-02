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
        /// Обновляет колонию: экономику, производство юнитов, проверку апгрейдов, защиту структур
        /// </summary>
        public async Task UpdateColonyAsync(Colony colony, float deltaTime)
        {
            if (colony == null)
                throw new ArgumentNullException(nameof(colony));

            try
            {
                // 1. Обновление производства ресурсов
                _economySimulator.UpdateProduction(colony, deltaTime);

                // 2. Обновление производства юнитов
                _unitEconomy.ProduceUnits(colony, deltaTime);

                // 3. Проверка возможности апгрейда
                if (await _stageManager.CanTransitionToNextStageAsync(colony))
                {
                    await _stageManager.TransitionToNextStageAsync(colony);
                }

                // 4. Защита структур от decay (каждый час)
                if (ShouldMaintainStructures(colony))
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
        /// Создает новую колонию
        /// </summary>
        public async Task<Colony> CreateColonyAsync(string playfield, Vector3 position, int factionId)
        {
            _logger.Info($"Creating new colony on '{playfield}' at {position}");

            var colony = await _stageManager.InitializeColonyAsync(playfield, position, factionId);

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
