using System;
using System.Threading.Tasks;
using Eleon.Modding;
using NLog;

namespace GalacticExpansion.Core.Gateway
{
    /// <summary>
    /// Основная реализация шлюза для взаимодействия с Empyrion ModAPI.
    /// Объединяет SequenceManager, RequestQueue и RateLimiter для надежной
    /// отправки запросов и обработки событий.
    /// 
    /// Архитектура:
    /// 1. Запросы добавляются в RequestQueue с приоритетом
    /// 2. RequestQueue контролирует частоту через RateLimiter
    /// 3. SequenceManager сопоставляет запросы с ответами через SeqNr
    /// 4. События от ModAPI обрабатываются и пробрасываются подписчикам
    /// 
    /// Thread-safe: можно вызывать из любого потока.
    /// </summary>
    public class EmpyrionGateway : IEmpyrionGateway
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        private readonly ModGameAPI _modApi;
        private readonly SequenceManager _sequenceManager;
        private readonly RateLimiter _rateLimiter;
        private readonly RequestQueue _requestQueue;
        
        private bool _isRunning;

        /// <summary>
        /// Событие получения данных от игры
        /// </summary>
        public event EventHandler<GameEventArgs>? GameEventReceived;

        /// <summary>
        /// Проверяет, запущен ли Gateway
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="modApi">Интерфейс ModAPI от Empyrion</param>
        /// <param name="maxRequestsPerSecond">Максимальное количество запросов в секунду</param>
        public EmpyrionGateway(ModGameAPI modApi, int maxRequestsPerSecond = 10)
        {
            _modApi = modApi ?? throw new ArgumentNullException(nameof(modApi));
            _sequenceManager = new SequenceManager();
            _rateLimiter = new RateLimiter(maxRequestsPerSecond);
            _requestQueue = new RequestQueue(_rateLimiter, maxConcurrentRequests: 1);

            Logger.Info("EmpyrionGateway created");
        }

        /// <summary>
        /// Запускает Gateway
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                Logger.Warn("Gateway is already running");
                return;
            }

            _requestQueue.Start();
            _isRunning = true;
            
            Logger.Info("EmpyrionGateway started successfully");
        }

        /// <summary>
        /// Останавливает Gateway
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
            {
                Logger.Warn("Gateway is not running");
                return;
            }

            Logger.Info("Stopping EmpyrionGateway...");

            _isRunning = false;
            
            // Останавливаем очередь запросов
            _requestQueue.StopAsync().Wait();
            
            // Отменяем все ожидающие запросы
            _sequenceManager.CancelAll();

            Logger.Info("EmpyrionGateway stopped");
        }

        /// <summary>
        /// Отправляет асинхронный запрос к ModAPI
        /// </summary>
        public Task<TResponse> SendRequestAsync<TResponse>(
            CmdId requestId, 
            object? data = null, 
            int timeoutMs = 5000)
        {
            if (!_isRunning)
            {
                throw new InvalidOperationException("Gateway is not running. Call Start() first.");
            }

            // Создаем TaskCompletionSource для ожидания ответа
            var seqNr = _sequenceManager.GetNextSequence();
            var responseTask = _sequenceManager.RegisterResponse<TResponse>(seqNr, timeoutMs);

            // Определяем приоритет запроса на основе типа
            var priority = GetRequestPriority(requestId);

            // Добавляем запрос в очередь
            _requestQueue.Enqueue(async () =>
            {
                try
                {
                    Logger.Debug($"Sending request: {requestId} (SeqNr: {seqNr}, Priority: {priority})");
                    
                    // Отправляем запрос через ModAPI
                    _modApi.Game_Request(requestId, seqNr, data);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error sending request: {requestId} (SeqNr: {seqNr})");
                    _sequenceManager.CompleteWithError(seqNr, ex);
                }
            }, priority);

            return responseTask;
        }

        /// <summary>
        /// Обрабатывает событие от ModAPI.
        /// Вызывается из ModInterface.Game_Event().
        /// </summary>
        public void HandleEvent(CmdId eventId, ushort seqNr, object data)
        {
            try
            {
                Logger.Debug($"Received event: {eventId} (SeqNr: {seqNr})");

                // Пытаемся завершить ожидающий запрос с этим SeqNr
                // Используем динамический тип, так как мы не знаем точный тип ответа
                var completed = TryCompleteResponse(seqNr, data);

                if (!completed)
                {
                    // Это не ответ на запрос, а самостоятельное событие
                    Logger.Debug($"Event {eventId} is not a response, broadcasting to subscribers");
                }

                // Пробрасываем событие подписчикам в любом случае
                GameEventReceived?.Invoke(this, new GameEventArgs(eventId, seqNr, data));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error handling event: {eventId} (SeqNr: {seqNr})");
            }
        }

        /// <summary>
        /// Пытается завершить ожидающий ответ с использованием рефлексии
        /// </summary>
        private bool TryCompleteResponse(ushort seqNr, object data)
        {
            try
            {
                // Получаем тип данных
                var dataType = data.GetType();
                
                // Вызываем CompleteResponse<T> с правильным типом через рефлексию
                var method = typeof(SequenceManager).GetMethod(nameof(SequenceManager.CompleteResponse));
                var genericMethod = method?.MakeGenericMethod(dataType);
                var result = genericMethod?.Invoke(_sequenceManager, new[] { seqNr, data });
                
                return result is bool boolResult && boolResult;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error completing response for SeqNr {seqNr}");
                return false;
            }
        }

        /// <summary>
        /// Определяет приоритет запроса на основе типа команды
        /// </summary>
        private RequestPriority GetRequestPriority(CmdId requestId)
        {
            return requestId switch
            {
                // Критические операции (спавн/удаление)
                CmdId.Request_Entity_Spawn => RequestPriority.Critical,
                CmdId.Request_Entity_Destroy => RequestPriority.Critical,
                CmdId.Request_Structure_Touch => RequestPriority.Critical,
                
                // Высокий приоритет (получение информации о структурах)
                CmdId.Request_GlobalStructure_List => RequestPriority.High,
                CmdId.Request_GlobalStructure_Update => RequestPriority.High,
                CmdId.Request_Structure_BlockStatistics => RequestPriority.High,
                
                // Низкий приоритет (фоновые операции)
                CmdId.Request_Player_List => RequestPriority.Low,
                CmdId.Request_Player_Info => RequestPriority.Low,
                
                // Все остальное - нормальный приоритет
                _ => RequestPriority.Normal
            };
        }

        /// <summary>
        /// Получает статистику Gateway
        /// </summary>
        public GatewayStatistics GetStatistics()
        {
            return new GatewayStatistics
            {
                IsRunning = _isRunning,
                PendingRequests = _sequenceManager.PendingCount,
                QueuedRequests = _requestQueue.GetTotalQueueSize(),
                AvailableTokens = _rateLimiter.AvailableTokens
            };
        }
    }

    /// <summary>
    /// Статистика работы Gateway
    /// </summary>
    public class GatewayStatistics
    {
        /// <summary>
        /// Gateway запущен
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// Количество запросов, ожидающих ответа
        /// </summary>
        public int PendingRequests { get; set; }

        /// <summary>
        /// Количество запросов в очереди
        /// </summary>
        public int QueuedRequests { get; set; }

        /// <summary>
        /// Доступные токены rate limiter
        /// </summary>
        public float AvailableTokens { get; set; }

        /// <summary>
        /// Преобразует статистику Gateway в строковое представление.
        /// </summary>
        /// <returns>Строка с информацией о состоянии Gateway</returns>
        public override string ToString()
        {
            return $"Gateway [Running: {IsRunning}, Pending: {PendingRequests}, Queued: {QueuedRequests}, Tokens: {AvailableTokens:F1}]";
        }
    }
}
