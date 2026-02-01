using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Gateway;
using GalacticExpansion.Core.Simulation;
using GalacticExpansion.Core.Simulation.Events;
using GalacticExpansion.Models;
using NLog;

namespace GalacticExpansion.Core.Tracking
{
    /// <summary>
    /// Отслеживает структуры на сервере и детектирует их создание/уничтожение.
    /// Периодически запрашивает список всех структур и сравнивает с предыдущим состоянием.
    /// </summary>
    public class StructureTracker : IStructureTracker
    {
        private readonly IEmpyrionGateway _gateway;
        private readonly IEventBus _eventBus;
        private readonly ILogger _logger;

        // Кэш структур по ID
        private readonly ConcurrentDictionary<int, GlobalStructureInfo> _structuresById;
        
        // Индекс структур по playfield
        private readonly ConcurrentDictionary<string, HashSet<int>> _structuresByPlayfield;

        // Время последнего обновления
        private DateTime _lastRefreshTime;
        
        // Интервал обновления (в секундах)
        private const int RefreshIntervalSeconds = 10;
        
        // Защита от ложных срабатываний при старте (в секундах)
        private const int InitialGracePeriodSeconds = 30;
        private DateTime _startTime;
        private bool _isInitialLoad = true;

        /// <inheritdoc/>
        public string ModuleName => "StructureTracker";

        /// <inheritdoc/>
        public int UpdatePriority => 20; // После PlayerTracker

        /// <summary>
        /// Создает новый экземпляр StructureTracker.
        /// </summary>
        /// <param name="gateway">Шлюз для взаимодействия с Empyrion</param>
        /// <param name="eventBus">Шина событий</param>
        /// <param name="logger">Логгер</param>
        public StructureTracker(
            IEmpyrionGateway gateway,
            IEventBus eventBus,
            ILogger logger)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _structuresById = new ConcurrentDictionary<int, GlobalStructureInfo>();
            _structuresByPlayfield = new ConcurrentDictionary<string, HashSet<int>>();
            _lastRefreshTime = DateTime.MinValue;
            _startTime = DateTime.UtcNow;

            _logger.Debug("StructureTracker created");
        }

        /// <inheritdoc/>
        public async Task InitializeAsync(SimulationState state)
        {
            _logger.Info("StructureTracker initializing...");

            _startTime = DateTime.UtcNow;
            _isInitialLoad = true;

            // Первоначальная загрузка структур
            await RefreshStructuresAsync();

            _logger.Info($"StructureTracker initialized with {_structuresById.Count} structures");
        }

        /// <inheritdoc/>
        public void OnSimulationUpdate(SimulationContext context)
        {
            // Проверяем, нужно ли обновить список структур
            var timeSinceLastRefresh = (DateTime.UtcNow - _lastRefreshTime).TotalSeconds;
            
            if (timeSinceLastRefresh >= RefreshIntervalSeconds)
            {
                _logger.Trace("StructureTracker: Refreshing structures...");
                _ = Task.Run(async () => await RefreshStructuresAsync());
            }
        }

        /// <inheritdoc/>
        public Task ShutdownAsync()
        {
            _logger.Info("StructureTracker shutting down...");

            // Очищаем кэш
            _structuresById.Clear();
            _structuresByPlayfield.Clear();

            _logger.Info("StructureTracker shut down");
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task RefreshStructuresAsync()
        {
            try
            {
                _logger.Debug("Requesting global structure list...");

                // Запрашиваем список всех структур
                var structures = await _gateway.SendRequestAsync<GlobalStructureList>(
                    CmdId.Request_GlobalStructure_List,
                    null,
                    timeoutMs: 10000
                );

                if (structures?.globalStructures == null)
                {
                    _logger.Warn("Received null structure list from gateway");
                    return;
                }

                // globalStructures - это Dictionary<string, List<GlobalStructureInfo>>
                // где ключ - название playfield, значение - список структур на этом playfield
                var totalCount = structures.globalStructures.Values.Sum(list => list.Count);
                _logger.Debug($"Received {totalCount} structures across {structures.globalStructures.Count} playfields");

                // Сохраняем старый список для детектирования изменений
                var oldStructureIds = new HashSet<int>(_structuresById.Keys);
                var newStructureIds = new HashSet<int>();

                // Обновляем кэш - проходим по всем playfield'ам
                foreach (var playfieldEntry in structures.globalStructures)
                {
                    var playfieldName = playfieldEntry.Key;
                    var structureList = playfieldEntry.Value;

                    foreach (var structure in structureList)
                    {
                        newStructureIds.Add(structure.id);
                        var isNew = !_structuresById.ContainsKey(structure.id);
                        
                        _structuresById[structure.id] = structure;

                        // Обновляем индекс по playfield
                        var playfieldStructures = _structuresByPlayfield.GetOrAdd(
                            playfieldName, 
                            _ => new HashSet<int>()
                        );
                        playfieldStructures.Add(structure.id);

                        // Публикуем событие создания (только после grace period)
                        if (isNew && !_isInitialLoad)
                        {
                            var timeSinceStart = (DateTime.UtcNow - _startTime).TotalSeconds;
                            if (timeSinceStart > InitialGracePeriodSeconds)
                            {
                                _logger.Info($"Structure created: {structure.id} ({structure.type}) on {playfieldName}");
                                
                                _eventBus.Publish(new StructureCreatedEvent
                                {
                                    EntityId = structure.id,
                                    Playfield = playfieldName,
                                    FactionId = structure.factionId,
                                    StructureType = structure.type.ToString(),
                                    EventTime = DateTime.UtcNow
                                });
                            }
                        }
                    }
                }

                // Детектируем уничтоженные структуры
                if (!_isInitialLoad)
                {
                    var destroyedIds = oldStructureIds.Except(newStructureIds);
                    
                    foreach (var destroyedId in destroyedIds)
                    {
                        if (_structuresById.TryRemove(destroyedId, out var destroyedStructure))
                        {
                            var timeSinceStart = (DateTime.UtcNow - _startTime).TotalSeconds;
                            if (timeSinceStart > InitialGracePeriodSeconds)
                            {
                                // Используем свойство playfield из структуры
                                var playfieldName = GetPlayfieldFromStructure(destroyedStructure);
                                _logger.Info($"Structure destroyed: {destroyedId} ({destroyedStructure.type}) on {playfieldName}");
                                
                                _eventBus.Publish(new StructureDestroyedEvent
                                {
                                    EntityId = destroyedId,
                                    Playfield = playfieldName,
                                    FactionId = destroyedStructure.factionId,
                                    StructureType = destroyedStructure.type.ToString(),
                                    EventTime = DateTime.UtcNow
                                });
                            }

                            // Удаляем из индекса по playfield
                            var playfieldName2 = GetPlayfieldFromStructure(destroyedStructure);
                            if (_structuresByPlayfield.TryGetValue(playfieldName2, out var playfieldStructures))
                            {
                                playfieldStructures.Remove(destroyedId);
                            }
                        }
                    }
                }

                _lastRefreshTime = DateTime.UtcNow;
                
                // После первого успешного обновления снимаем флаг initial load
                if (_isInitialLoad)
                {
                    _isInitialLoad = false;
                    _logger.Info($"Initial structure load completed: {_structuresById.Count} structures");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error refreshing structures: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public List<GlobalStructureInfo> GetStructuresOnPlayfield(string playfield, int? factionId = null)
        {
            if (string.IsNullOrEmpty(playfield))
                return new List<GlobalStructureInfo>();

            if (!_structuresByPlayfield.TryGetValue(playfield, out var structureIds))
                return new List<GlobalStructureInfo>();

            var structures = new List<GlobalStructureInfo>();
            foreach (var id in structureIds)
            {
                if (_structuresById.TryGetValue(id, out var structure))
                {
                    structures.Add(structure);
                }
            }

            if (factionId.HasValue)
            {
                structures = structures.Where(s => s.factionId == factionId.Value).ToList();
            }

            return structures;
        }

        /// <inheritdoc/>
        public GlobalStructureInfo? GetStructure(int entityId)
        {
            if (_structuresById.TryGetValue(entityId, out var structure))
            {
                return structure;
            }
            return null;
        }

        /// <inheritdoc/>
        public bool StructureExists(int entityId)
        {
            return _structuresById.ContainsKey(entityId);
        }

        /// <summary>
        /// Получает название playfield из структуры.
        /// Использует рефлексию для получения свойства, так как имя может быть "playfield" или "Playfield".
        /// </summary>
        private string GetPlayfieldFromStructure(GlobalStructureInfo structure)
        {
            // Пробуем получить свойство через рефлексию
            var type = structure.GetType();
            var playfieldProp = type.GetProperty("playfield") ?? type.GetProperty("Playfield");
            
            if (playfieldProp != null)
            {
                var value = playfieldProp.GetValue(structure);
                return value?.ToString() ?? string.Empty;
            }

            return string.Empty;
        }
    }
}
