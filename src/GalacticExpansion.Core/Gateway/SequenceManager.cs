using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace GalacticExpansion.Core.Gateway
{
    /// <summary>
    /// Управляет sequence numbers для запросов к ModAPI и сопоставляет ответы с запросами.
    /// 
    /// Принцип работы:
    /// 1. При отправке запроса генерируется уникальный SeqNr
    /// 2. Создается TaskCompletionSource для ожидания ответа
    /// 3. При получении ответа с этим SeqNr, TaskCompletionSource завершается
    /// 4. Если ответ не получен в течение таймаута, запрос отменяется
    /// 
    /// Thread-safe: может использоваться из нескольких потоков одновременно.
    /// </summary>
    public class SequenceManager
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        // Текущий sequence number (начинаем с 1, так как 0 зарезервирован)
        private int _currentSeq = 0;

        // Словарь ожидающих ответов: SeqNr -> PendingResponse
        private readonly ConcurrentDictionary<ushort, PendingResponse> _pendingResponses = new ConcurrentDictionary<ushort, PendingResponse>();

        /// <summary>
        /// Генерирует следующий уникальный sequence number.
        /// Thread-safe.
        /// </summary>
        /// <returns>Уникальный SeqNr</returns>
        public ushort GetNextSequence()
        {
            // Атомарный инкремент с защитой от переполнения
            var next = Interlocked.Increment(ref _currentSeq);
            
            // ushort переполняется при 65535, начинаем с 1 снова
            if (next > ushort.MaxValue)
            {
                Interlocked.CompareExchange(ref _currentSeq, 1, next);
                next = 1;
            }

            return (ushort)next;
        }

        /// <summary>
        /// Регистрирует ожидание ответа на запрос.
        /// Создает TaskCompletionSource и настраивает таймаут.
        /// </summary>
        /// <typeparam name="T">Тип ожидаемого ответа</typeparam>
        /// <param name="seqNr">Sequence number запроса</param>
        /// <param name="timeoutMs">Таймаут в миллисекундах</param>
        /// <returns>Task, который завершится при получении ответа или таймауте</returns>
        public Task<T> RegisterResponse<T>(ushort seqNr, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<T>();
            var response = new PendingResponse
            {
                CompletionSource = tcs,
                Timeout = DateTime.UtcNow.AddMilliseconds(timeoutMs),
                RequestType = typeof(T).Name
            };

            if (!_pendingResponses.TryAdd(seqNr, response))
            {
                Logger.Warn($"SeqNr {seqNr} already registered. This should not happen!");
                throw new InvalidOperationException($"SeqNr {seqNr} is already in use");
            }

            // Настраиваем таймаут
            _ = Task.Run(async () =>
            {
                await Task.Delay(timeoutMs);
                
                // Если ответ все еще ожидается, отменяем его
                if (_pendingResponses.TryRemove(seqNr, out var timedOutResponse))
                {
                    Logger.Warn($"Request with SeqNr {seqNr} timed out after {timeoutMs}ms (type: {typeof(T).Name})");
                    
                    var timeoutException = new TimeoutException(
                        $"Request timed out after {timeoutMs}ms (SeqNr: {seqNr}, Type: {typeof(T).Name})");
                    
                    (timedOutResponse.CompletionSource as TaskCompletionSource<T>)?.TrySetException(timeoutException);
                }
            });

            Logger.Debug($"Registered response for SeqNr {seqNr} (type: {typeof(T).Name}, timeout: {timeoutMs}ms)");
            return tcs.Task;
        }

        /// <summary>
        /// Завершает ожидание ответа с указанным SeqNr.
        /// Вызывается при получении ответа от ModAPI.
        /// </summary>
        /// <typeparam name="T">Тип ответа</typeparam>
        /// <param name="seqNr">Sequence number ответа</param>
        /// <param name="data">Данные ответа</param>
        /// <returns>true, если ответ был успешно обработан; false, если SeqNr не найден</returns>
        public bool CompleteResponse<T>(ushort seqNr, T data)
        {
            if (_pendingResponses.TryRemove(seqNr, out var response))
            {
                Logger.Debug($"Completing response for SeqNr {seqNr} (type: {typeof(T).Name})");
                
                var tcs = response.CompletionSource as TaskCompletionSource<T>;
                if (tcs != null)
                {
                    tcs.TrySetResult(data);
                    return true;
                }
                else
                {
                    Logger.Error($"Type mismatch for SeqNr {seqNr}: expected {response.RequestType}, got {typeof(T).Name}");
                    return false;
                }
            }

            Logger.Warn($"Received response for unknown SeqNr {seqNr} (type: {typeof(T).Name}). Probably timed out or cancelled.");
            return false;
        }

        /// <summary>
        /// Завершает ожидание ответа с ошибкой.
        /// </summary>
        public bool CompleteWithError(ushort seqNr, Exception exception)
        {
            if (_pendingResponses.TryRemove(seqNr, out var response))
            {
                Logger.Debug($"Completing response with error for SeqNr {seqNr}: {exception.Message}");
                
                // Используем динамический вызов для установки исключения
                var tcsType = response.CompletionSource.GetType();
                var trySetExceptionMethod = tcsType.GetMethod("TrySetException", new[] { typeof(Exception) });
                trySetExceptionMethod?.Invoke(response.CompletionSource, new object[] { exception });
                
                return true;
            }

            return false;
        }

        /// <summary>
        /// Отменяет все ожидающие запросы.
        /// Вызывается при остановке Gateway.
        /// </summary>
        public void CancelAll()
        {
            Logger.Info($"Cancelling all pending requests ({_pendingResponses.Count} total)");

            foreach (var kvp in _pendingResponses)
            {
                var seqNr = kvp.Key;
                var response = kvp.Value;

                if (_pendingResponses.TryRemove(seqNr, out _))
                {
                    var cancelledException = new OperationCanceledException(
                        $"Request cancelled due to Gateway shutdown (SeqNr: {seqNr})");
                    
                    // Используем динамический вызов для установки исключения
                    var tcsType = response.CompletionSource.GetType();
                    var trySetExceptionMethod = tcsType.GetMethod("TrySetException", new[] { typeof(Exception) });
                    trySetExceptionMethod?.Invoke(response.CompletionSource, new object[] { cancelledException });
                }
            }

            _pendingResponses.Clear();
        }

        /// <summary>
        /// Получает количество ожидающих ответов
        /// </summary>
        public int PendingCount => _pendingResponses.Count;

        /// <summary>
        /// Представляет ожидающий ответ
        /// </summary>
        private class PendingResponse
        {
            /// <summary>
            /// TaskCompletionSource для завершения ожидания
            /// </summary>
            public object CompletionSource { get; set; } = null!;

            /// <summary>
            /// Время истечения таймаута
            /// </summary>
            public DateTime Timeout { get; set; }

            /// <summary>
            /// Тип запроса (для отладки)
            /// </summary>
            public string RequestType { get; set; } = string.Empty;
        }
    }
}
