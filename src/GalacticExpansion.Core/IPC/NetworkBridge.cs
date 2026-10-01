using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace GalacticExpansion.Core.IPC
{
    /// <summary>
    /// Управляет IPC между Dedi и PfServer.
    /// Сам ModApi.Network не вызывает: это делает IEmpyrionModChannel из сборки GalacticExpansion,
    /// иначе игра регистрирует колбэк под именем GalacticExpansion.Core и пакеты не доходят.
    /// </summary>
    public class NetworkBridge : IDisposable
    {
        private readonly IEmpyrionModChannel _channel;
        private readonly ILogger _logger;

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
        /// <param name="channel">Канал INetwork из сборки GalacticExpansion. Из Core вызывать ModApi.Network нельзя: игра привяжет колбэк к GalacticExpansion.Core.</param>
        /// <param name="logger">Логгер для записи диагностической информации</param>
        /// <exception cref="ArgumentNullException">Выбрасывается если channel или logger равны null</exception>
        public NetworkBridge(IEmpyrionModChannel channel, ILogger logger)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Инициализирует NetworkBridge для Dedi процесса.
        /// Регистрирует приёмник пакетов, которые пришли С playfield.
        /// Имя метода Empyrion — про источник пакета, не про процесс подписчика:
        /// dedicated слушает RegisterReceiverForPlayfieldPackets
        /// (так делает EmpyrionScripting.PlayerCommandsDediHelper).
        /// </summary>
        public void InitializeForDedi()
        {
            if (_isReceiverRegistered)
            {
                _logger.Warn("NetworkBridge already initialized");
                return;
            }

            _logger.Info("Initializing NetworkBridge for Dedi process");

            // Пакеты PfServer → Dedi (SendToDedicatedServer) приходят сюда.
            if (_channel.RegisterReceiverForPlayfieldPackets(OnDediPacketReceived))
            {
                _isReceiverRegistered = true;
                _logger.Info("✅ Dedi receiver registered (RegisterReceiverForPlayfieldPackets)");
            }
            else
            {
                _logger.Error("❌ Failed to register Dedi receiver for playfield packets");
            }
        }

        /// <summary>
        /// Инициализирует NetworkBridge для PfServer процесса.
        /// Слушает пакеты, созданные на dedicated: RegisterReceiverForDediPackets.
        /// Ответы уходят обратно через SendToDedicatedServer.
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

            // Пакеты Dedi → PfServer (SendToPlayfieldServer) приходят сюда.
            // RegisterReceiverForDediPackets вызывается на playfield: пакет создан на dedicated.
            if (_channel.RegisterReceiverForDediPackets(OnPlayfieldPacketReceived))
            {
                _isReceiverRegistered = true;
                _logger.Info($"✅ PfServer receiver registered for playfield '{playfieldName}' (RegisterReceiverForDediPackets)");
            }
            else
            {
                _logger.Error("❌ Failed to register PfServer receiver for dedi packets");
            }
        }

        /// <summary>
        /// Отправляет fire-and-forget уведомление от PfServer к Dedi (без ожидания ответа).
        /// Используется PfServer для отправки PlayfieldReadyNotification после загрузки playfield.
        /// Включает retry-логику (3 попытки с задержкой 500ms) для надёжности,
        /// т.к. Dedi receiver может ещё не быть полностью инициализирован в момент первой отправки.
        /// </summary>
        /// <param name="notification">IPC-сообщение для отправки</param>
        public void SendNotificationToDedi(IPCMessage notification)
        {
            if (notification == null)
                throw new ArgumentNullException(nameof(notification));

            var data = SerializeMessage(notification);
            var playfield = _currentPlayfield ?? "Unknown";

            _logger.Debug($"[PfServer] Attempting to send {notification.MessageType} to Dedi (channel: '{_channel.ChannelId}', playfield: '{playfield}', data size: {data.Length} bytes)");

            // Retry-логика: 3 попытки с задержкой 500ms между ними.
            // Dedi receiver может быть ещё не готов при первой попытке (timing issue).
            _ = Task.Run(async () =>
            {
                const int maxRetries = 3;
                const int retryDelayMs = 500;
                
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        if (_channel.SendToDedicatedServer(data, playfield))
                        {
                            _logger.Info($"[PfServer] Sent {notification.MessageType} to Dedi (playfield: {playfield}, attempt {attempt}/{maxRetries})");
                            return;
                        }
                        
                        _logger.Warn($"[PfServer] SendToDedicatedServer returned false for {notification.MessageType} (attempt {attempt}/{maxRetries})");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"[PfServer] Error sending {notification.MessageType} (attempt {attempt}/{maxRetries}): {ex.Message}");
                    }

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs);
                    }
                }
                
                _logger.Error($"[PfServer] Failed to send {notification.MessageType} to Dedi after {maxRetries} attempts (playfield: {playfield})");
            });
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
                if (!_channel.SendToPlayfieldServer(playfieldName, data))
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
                // Empyrion отдаёт этому callback все пакеты с playfield, не только наши.
                // Чужой канал (другой мод) не десериализуем.
                if (!string.Equals(sender, _channel.ChannelId, StringComparison.Ordinal))
                {
                    _logger.Debug($"[Dedi] Ignoring packet from sender '{sender}'");
                    return;
                }

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
                // На playfield приходят все пакеты с dedicated. Берём только канал этого мода.
                if (!string.Equals(sender, _channel.ChannelId, StringComparison.Ordinal))
                {
                    _logger.Debug($"[PfServer] Ignoring packet from sender '{sender}'");
                    return;
                }

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
                            if (_channel.SendToDedicatedServer(responseData, _currentPlayfield ?? playfieldName))
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
                        _channel.SendToDedicatedServer(errorData, _currentPlayfield ?? playfieldName);
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
        /// Десериализует byte[] в конкретный класс IPC-сообщения.
        /// IPCMessage абстрактный: JsonConvert.DeserializeObject&lt;IPCMessage&gt; падает
        /// с «Could not create an instance», ещё до чтения полей.
        /// Сначала читаем поле type из JSON (JObject экземпляр не создаёт),
        /// и только потом десериализуем наследника: SpawnStructureRequest и остальные.
        /// Поле type в JSON не первое: Newtonsoft пишет свойства наследника (pf, prefab)
        /// раньше свойств базового класса.
        /// </summary>
        private IPCMessage? DeserializeMessage(byte[] data)
        {
            try
            {
                var json = Encoding.UTF8.GetString(data);

                // JObject.Parse только разбирает текст. Объект IPCMessage здесь не создаётся.
                var messageType = JObject.Parse(json)["type"]?.Value<string>();
                if (string.IsNullOrEmpty(messageType))
                {
                    _logger.Error("IPC JSON has no type field");
                    return null;
                }

                return messageType switch
                {
                    "SpawnStructure" => JsonConvert.DeserializeObject<SpawnStructureRequest>(json),
                    "SpawnStructureResponse" => JsonConvert.DeserializeObject<SpawnStructureResponse>(json),
                    "SpawnNPC" => JsonConvert.DeserializeObject<SpawnNPCRequest>(json),
                    "SpawnNPCResponse" => JsonConvert.DeserializeObject<SpawnNPCResponse>(json),
                    "PlayfieldReady" => JsonConvert.DeserializeObject<PlayfieldReadyRequest>(json),
                    "PlayfieldReadyResponse" => JsonConvert.DeserializeObject<PlayfieldReadyResponse>(json),
                    "PlayfieldReadyNotification" => JsonConvert.DeserializeObject<PlayfieldReadyNotification>(json),
                    _ => throw new InvalidOperationException($"Unknown message type: {messageType}")
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
