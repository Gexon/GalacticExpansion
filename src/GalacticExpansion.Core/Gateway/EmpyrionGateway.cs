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

            // Сначала отменяем все ожидающие ответы, чтобы вызывающие не висели на SendRequestAsync
            _sequenceManager.CancelAll();

            // Останавливаем очередь с таймаутом: при shutdown игра может не отвечать на запросы,
            // и синхронный Wait() без таймаута может привести к зависанию сервера
            const int shutdownTimeoutMs = 5000;
            var stopTask = _requestQueue.StopAsync();
            if (!stopTask.Wait(shutdownTimeoutMs))
            {
                Logger.Warn($"RequestQueue did not stop within {shutdownTimeoutMs}ms. Proceeding with shutdown.");
            }

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

                // Event_Error — игра вернула ошибку вместо ожидаемого ответа (например, спавн не удался)
                if (eventId == CmdId.Event_Error)
                {
                    var errorMessage = GetErrorMessageFromErrorInfo(data);
                    Logger.Warn($"Game returned error for SeqNr {seqNr}: {errorMessage}");
                    var completed = _sequenceManager.CompleteWithError(seqNr, new InvalidOperationException($"Game error (SeqNr {seqNr}): {errorMessage}"));
                    if (completed)
                    {
                        GameEventReceived?.Invoke(this, new GameEventArgs(eventId, seqNr, data));
                        return;
                    }
                }

                // Request_Structure_Touch игра подтверждает событием Event_Ok без тела.
                // Пустой data нельзя разобрать рефлексией, но это успешный ответ: закрываем ожидание сразу,
                // иначе тик колонии простаивает до таймаута в 3 секунды.
                if (eventId == CmdId.Event_Ok && data == null)
                {
                    var completed = _sequenceManager.CompleteWithoutPayload(seqNr);
                    if (completed)
                    {
                        Logger.Debug($"Event_Ok with empty data completed SeqNr {seqNr}");
                        GameEventReceived?.Invoke(this, new GameEventArgs(eventId, seqNr, data!));
                        return;
                    }
                }

                // Пытаемся завершить ожидающий запрос с этим SeqNr
                var completedResponse = TryCompleteResponse(seqNr, data!);

                if (!completedResponse)
                {
                    // Это не ответ на запрос, а самостоятельное событие
                    Logger.Debug($"Event {eventId} is not a response, broadcasting to subscribers");
                }

                // Пробрасываем событие подписчикам в любом случае.
                // data! — пустой Event_Ok тоже доходит сюда, если на этот номер никто не ждал.
                GameEventReceived?.Invoke(this, new GameEventArgs(eventId, seqNr, data!));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error handling event: {eventId} (SeqNr: {seqNr})");
            }
        }

        /// <summary>
        /// Извлекает текст ошибки из объекта ErrorInfo (игра возвращает при Event_Error).
        /// </summary>
        private static string GetErrorMessageFromErrorInfo(object data)
        {
            if (data == null) return "Unknown error (null)";
            // Официальный контракт Event_Error: data — ErrorInfo с полем errorType (enum).
            // Даёт в логах и исключениях понятный код ошибки (например, EntityNotLocalToPlayfield).
            if (data is ErrorInfo eInfo)
                return eInfo.errorType.ToString();
            var type = data.GetType();
            var msgProp = type.GetProperty("msg") ?? type.GetProperty("Msg") ?? type.GetProperty("message") ?? type.GetProperty("Message");
            if (msgProp != null && msgProp.CanRead)
            {
                var value = msgProp.GetValue(data);
                if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                    return value.ToString()!;
            }
            var idProp = type.GetProperty("id") ?? type.GetProperty("Id");
            if (idProp != null && idProp.CanRead)
            {
                var id = idProp.GetValue(data);
                if (id != null) return $"Error id: {id}";
            }
            return data.ToString() ?? "Unknown error";
        }

        /// <summary>
        /// Пытается завершить ожидающий ответ с использованием рефлексии.
        /// Пустой Event_Ok обрабатывается раньше, в HandleEvent: он завершает ожидание без тела.
        /// Сюда такой ответ доходит только если на этот SeqNr никто не ждёт.
        /// </summary>
        private bool TryCompleteResponse(ushort seqNr, object data)
        {
            try
            {
                // Event_Ok от Empyrion приходит с data = null — рефлексия невозможна
                if (data == null)
                {
                    Logger.Debug($"TryCompleteResponse: data is null for SeqNr {seqNr} (likely Event_Ok), skipping");
                    return false;
                }

                var dataType = data.GetType();
                
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

                // Касание структуры — фоновый сброс таймера распада, раз в час.
                // Не стоит в одной очереди со спавном и удалением.
                CmdId.Request_Structure_Touch => RequestPriority.Low,
                
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
