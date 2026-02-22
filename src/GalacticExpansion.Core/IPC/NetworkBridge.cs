using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Eleon.Modding;
using Newtonsoft.Json;
using NLog;

namespace GalacticExpansion.Core.IPC
{
    /// <summary>
    /// Управляет IPC коммуникацией между Dedi и PfServer процессами через INetwork.
    /// Предоставляет async/await API для отправки запросов и получения ответов.
    /// </summary>
    public class NetworkBridge : IDisposable
    {
        private readonly IModApi _modApi;
        private readonly ILogger _logger;
        private readonly string _receiverId = "GLEX"; // Уникальный ID для регистрации receiver

        // Dictionary для tracking pending requests: RequestId -> TaskCompletionSource
        private readonly ConcurrentDictionary<Guid, PendingRequest> _pendingRequests = new ConcurrentDictionary<Guid, PendingRequest>();

        // Текущий playfield (для PfServer процесса)
        private string? _currentPlayfield;

        // Флаг для отслеживания регистрации receivers
        private bool _isReceiverRegistered;

        /// <summary>
        /// Обработчик запросов для PfServer процесса.
        /// Вызывается когда PfServer получает запрос от Dedi.
        /// </summary>
        public event Func<IPCMessage, string, Task<IPCMessage?>>? OnRequestReceived;

        /// <summary>
        /// Событие: PfServer сообщил что playfield полностью загружен и готов к spawn-операциям.
        /// Dedi подписывается на это событие для вызова EnsurePlayfieldColoniesSpawnedAsync.
        /// Параметр — название playfield.
        /// </summary>
        public event Action<string>? OnPlayfieldReadyReceived;

        /// <summary>
        /// Инициализирует новый экземпляр NetworkBridge для управления IPC коммуникацией.
        /// </summary>
        /// <param name="modApi">API мода для доступа к сетевым функциям Empyrion</param>
        /// <param name="logger">Логгер для записи диагностической информации</param>
        /// <exception cref="ArgumentNullException">Выбрасывается если modApi или logger равны null</exception>
        public NetworkBridge(IModApi modApi, ILogger logger)
        {
            _modApi = modApi ?? throw new ArgumentNullException(nameof(modApi));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Инициализирует NetworkBridge для Dedi процесса.
        /// Регистрирует receiver для получения ответов от PfServer.
        /// </summary>
        public void InitializeForDedi()
        {
            if (_isReceiverRegistered)
            {
                _logger.Warn("NetworkBridge already initialized");
                return;
            }

            _logger.Info("Initializing NetworkBridge for Dedi process");
            
            if (_modApi.Network.RegisterReceiverForDediPackets(OnDediPacketReceived))
            {
                _isReceiverRegistered = true;
                _logger.Info("✅ Dedi receiver registered successfully");
            }
            else
            {
                _logger.Error("❌ Failed to register Dedi receiver");
            }
        }

        /// <summary>
        /// Инициализирует NetworkBridge для PfServer процесса.
        /// Регистрирует receiver для получения запросов от Dedi и отправки ответов.
        /// </summary>
        /// <param name="playfieldName">Название playfield этого PfServer процесса</param>
        public void InitializeForPlayfieldServer(string playfieldName)
        {
            if (_isReceiverRegistered)
            {
                _logger.Warn("NetworkBridge already initialized");
                return;
            }

            if (string.IsNullOrEmpty(playfieldName))
                throw new ArgumentException("Playfield name cannot be empty", nameof(playfieldName));

            _currentPlayfield = playfieldName;
            _logger.Info($"Initializing NetworkBridge for PfServer process (playfield: {playfieldName})");

            if (_modApi.Network.RegisterReceiverForPlayfieldPackets(OnPlayfieldPacketReceived))
            {
                _isReceiverRegistered = true;
                _logger.Info($"✅ PfServer receiver registered for playfield '{playfieldName}'");
            }
            else
            {
                _logger.Error("❌ Failed to register PfServer receiver");
            }
        }

        /// <summary>
        /// Отправляет fire-and-forget уведомление от PfServer к Dedi (без ожидания ответа).
        /// Используется PfServer для отправки PlayfieldReadyNotification после загрузки playfield.
        /// </summary>
        /// <param name="notification">IPC-сообщение для отправки</param>
        public void SendNotificationToDedi(IPCMessage notification)
        {
            if (notification == null)
                throw new ArgumentNullException(nameof(notification));

            var data = SerializeMessage(notification);
            var playfield = _currentPlayfield ?? "Unknown";

            if (_modApi.Network.SendToDedicatedServer(_receiverId, data, playfield))
            {
                _logger.Info($"[PfServer] Sent {notification.MessageType} to Dedi (playfield: {playfield})");
            }
            else
            {
                _logger.Error($"[PfServer] Failed to send {notification.MessageType} to Dedi (playfield: {playfield})");
            }
        }

        /// <summary>
        /// Отправляет запрос от Dedi к PfServer и ждет ответ.
        /// Используется только в Dedi процессе.
        /// </summary>
        /// <typeparam name="TResponse">Тип ожидаемого ответа</typeparam>
        /// <param name="request">Запрос для отправки</param>
        /// <param name="playfieldName">Название playfield где нужно выполнить запрос</param>
        /// <param name="timeoutMs">Таймаут ожидания ответа (по умолчанию 15 секунд)</param>
        /// <returns>Ответ от PfServer</returns>
        public async Task<TResponse> SendRequestToPlayfieldAsync<TResponse>(IPCMessage request, string playfieldName, int timeoutMs = 15000)
            where TResponse : IPCMessage
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrEmpty(playfieldName))
                throw new ArgumentException("Playfield name cannot be empty", nameof(playfieldName));

            // Генерируем RequestId если еще не установлен
            if (request.RequestId == Guid.Empty)
                request.RequestId = Guid.NewGuid();

            _logger.Debug($"Sending {request.MessageType} request to playfield '{playfieldName}' (RequestId={request.RequestId})");

            // Регистрируем pending request
            var pendingRequest = new PendingRequest
            {
                RequestId = request.RequestId,
                TaskCompletionSource = new TaskCompletionSource<IPCMessage>(),
                CreatedAt = DateTime.UtcNow
            };

            if (!_pendingRequests.TryAdd(request.RequestId, pendingRequest))
            {
                throw new InvalidOperationException($"Request with ID {request.RequestId} already exists");
            }

            try
            {
                // Сериализуем и отправляем
                var data = SerializeMessage(request);
                if (!_modApi.Network.SendToPlayfieldServer(_receiverId, playfieldName, data))
                {
                    throw new InvalidOperationException($"Failed to send message to playfield '{playfieldName}'");
                }

                // Ждем ответ с таймаутом
                using var cts = new CancellationTokenSource(timeoutMs);
                cts.Token.Register(() => pendingRequest.TaskCompletionSource.TrySetCanceled());

                var response = await pendingRequest.TaskCompletionSource.Task;
                
                if (response is TResponse typedResponse)
                {
                    _logger.Debug($"Received response for RequestId={request.RequestId}");
                    return typedResponse;
                }

                throw new InvalidOperationException($"Unexpected response type: {response.GetType().Name}");
            }
            catch (OperationCanceledException)
            {
                _logger.Error($"Timeout waiting for response (RequestId={request.RequestId}, timeout={timeoutMs}ms)");
                throw new TimeoutException($"No response from playfield '{playfieldName}' after {timeoutMs}ms");
            }
            finally
            {
                // Очищаем pending request
                _pendingRequests.TryRemove(request.RequestId, out _);
            }
        }

        /// <summary>
        /// Callback для приема пакетов в Dedi процессе (ответы от PfServer).
        /// </summary>
        private void OnDediPacketReceived(string sender, string playfieldName, byte[] data)
        {
            try
            {
                _logger.Debug($"[Dedi] Received packet from '{sender}' (playfield: {playfieldName}, size: {data.Length} bytes)");

                var message = DeserializeMessage(data);
                if (message == null)
                {
                    _logger.Warn("Failed to deserialize message");
                    return;
                }

                _logger.Debug($"[Dedi] Message type: {message.MessageType}, RequestId: {message.RequestId}");

                // PlayfieldReadyNotification — fire-and-forget уведомление от PfServer,
                // не имеет pending request. Вызываем событие для обработки на Dedi.
                if (message is PlayfieldReadyNotification readyNotification)
                {
                    _logger.Info($"[Dedi] Received PlayfieldReadyNotification for '{readyNotification.Playfield}' from PfServer");
                    OnPlayfieldReadyReceived?.Invoke(readyNotification.Playfield);
                    return;
                }

                // Находим pending request (для request/response сообщений)
                if (_pendingRequests.TryRemove(message.RequestId, out var pendingRequest))
                {
                    // Завершаем Task с полученным ответом
                    pendingRequest.TaskCompletionSource.TrySetResult(message);
                    _logger.Debug($"[Dedi] Completed pending request {message.RequestId}");
                }
                else
                {
                    _logger.Warn($"[Dedi] Received response for unknown RequestId: {message.RequestId}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[Dedi] Error processing packet");
            }
        }

        /// <summary>
        /// Callback для приема пакетов в PfServer процессе (запросы от Dedi).
        /// Обрабатывает запрос и отправляет ответ обратно.
        /// </summary>
        private void OnPlayfieldPacketReceived(string sender, string playfieldName, byte[] data)
        {
            try
            {
                _logger.Debug($"[PfServer] Received packet from '{sender}' (playfield: {playfieldName}, size: {data.Length} bytes)");

                var message = DeserializeMessage(data);
                if (message == null)
                {
                    _logger.Warn("[PfServer] Failed to deserialize message");
                    return;
                }

                _logger.Info($"[PfServer] Processing {message.MessageType} (RequestId={message.RequestId})");

                // Асинхронно обрабатываем запрос (не блокируем callback)
                Task.Run(async () =>
                {
                    try
                    {
                        // Вызываем обработчик запроса
                        var handler = OnRequestReceived;
                        var response = handler != null ? await handler.Invoke(message, playfieldName) : null;
                        
                        if (response != null)
                        {
                            // Копируем RequestId из запроса в ответ
                            response.RequestId = message.RequestId;

                            // Отправляем ответ обратно в Dedi
                            var responseData = SerializeMessage(response);
                            if (_modApi.Network.SendToDedicatedServer(_receiverId, responseData, _currentPlayfield ?? playfieldName))
                            {
                                _logger.Debug($"[PfServer] Response sent for RequestId={message.RequestId}");
                            }
                            else
                            {
                                _logger.Error($"[PfServer] Failed to send response for RequestId={message.RequestId}");
                            }
                        }
                        else
                        {
                            _logger.Warn($"[PfServer] No response generated for RequestId={message.RequestId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"[PfServer] Error handling request {message.RequestId}");

                        // Отправляем error response
                        var errorResponse = new SpawnStructureResponse
                        {
                            RequestId = message.RequestId,
                            Success = false,
                            ErrorMessage = $"Internal error: {ex.Message}",
                            Playfield = playfieldName
                        };

                        var errorData = SerializeMessage(errorResponse);
                        _modApi.Network.SendToDedicatedServer(_receiverId, errorData, _currentPlayfield ?? playfieldName);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[PfServer] Error in packet callback");
            }
        }

        /// <summary>
        /// Сериализует IPC сообщение в byte[] для отправки через INetwork.
        /// Использует JSON для простоты и надежности.
        /// </summary>
        private byte[] SerializeMessage(IPCMessage message)
        {
            var json = JsonConvert.SerializeObject(message, Formatting.None);
            return Encoding.UTF8.GetBytes(json);
        }

        /// <summary>
        /// Десериализует byte[] в IPC сообщение.
        /// Определяет тип сообщения по полю MessageType и десериализует в соответствующий класс.
        /// </summary>
        private IPCMessage? DeserializeMessage(byte[] data)
        {
            try
            {
                var json = Encoding.UTF8.GetString(data);
                
                // Сначала десериализуем как базовый класс чтобы получить MessageType
                var baseMessage = JsonConvert.DeserializeObject<IPCMessage>(json);
                if (baseMessage == null)
                    return null;

                // Десериализуем в правильный тип на основе MessageType
                return baseMessage.MessageType switch
                {
                    "SpawnStructure" => JsonConvert.DeserializeObject<SpawnStructureRequest>(json),
                    "SpawnStructureResponse" => JsonConvert.DeserializeObject<SpawnStructureResponse>(json),
                    "SpawnNPC" => JsonConvert.DeserializeObject<SpawnNPCRequest>(json),
                    "SpawnNPCResponse" => JsonConvert.DeserializeObject<SpawnNPCResponse>(json),
                    "PlayfieldReady" => JsonConvert.DeserializeObject<PlayfieldReadyRequest>(json),
                    "PlayfieldReadyResponse" => JsonConvert.DeserializeObject<PlayfieldReadyResponse>(json),
                    "PlayfieldReadyNotification" => JsonConvert.DeserializeObject<PlayfieldReadyNotification>(json),
                    _ => throw new InvalidOperationException($"Unknown message type: {baseMessage.MessageType}")
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to deserialize message");
                return null;
            }
        }

        /// <summary>
        /// Освобождает ресурсы NetworkBridge.
        /// Завершает все ожидающие запросы с отменой и очищает внутренние коллекции.
        /// </summary>
        public void Dispose()
        {
            // Завершаем все pending requests
            foreach (var pending in _pendingRequests.Values)
            {
                pending.TaskCompletionSource.TrySetCanceled();
            }
            _pendingRequests.Clear();
        }

        /// <summary>
        /// Внутренний класс для отслеживания pending requests
        /// </summary>
        private class PendingRequest
        {
            public Guid RequestId { get; set; }
            public TaskCompletionSource<IPCMessage> TaskCompletionSource { get; set; } = new TaskCompletionSource<IPCMessage>();
            public DateTime CreatedAt { get; set; }
        }
    }
}
