дusing System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace GalacticExpansion.Core.Gateway
{
    /// <summary>
    /// Очередь запросов с приоритетами для отправки в ModAPI.
    /// Обрабатывает запросы последовательно с учетом приоритета и rate limiting.
    /// 
    /// Особенности:
    /// - Запросы с более высоким приоритетом обрабатываются первыми
    /// - Ограничение одновременных запросов (по умолчанию 1)
    /// - Интеграция с RateLimiter для контроля частоты запросов
    /// - Graceful shutdown с завершением текущих запросов
    /// 
    /// Thread-safe: может использоваться из нескольких потоков.
    /// </summary>
    public class RequestQueue
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        // Очередь с приоритетами (меньший приоритет = выше в очереди)
        private readonly SortedDictionary<int, Queue<QueuedRequest>> _priorityQueues = new();
        
        private readonly RateLimiter _rateLimiter;
        private readonly SemaphoreSlim _concurrencySemaphore;
        private readonly object _lockObject = new object();
        
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _processingTask;
        private bool _isRunning;

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="rateLimiter">Rate limiter для контроля частоты запросов</param>
        /// <param name="maxConcurrentRequests">Максимальное количество одновременных запросов</param>
        public RequestQueue(RateLimiter rateLimiter, int maxConcurrentRequests = 1)
        {
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _concurrencySemaphore = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);

            // Инициализируем очереди для каждого приоритета
            foreach (RequestPriority priority in Enum.GetValues(typeof(RequestPriority)))
            {
                _priorityQueues[(int)priority] = new Queue<QueuedRequest>();
            }

            Logger.Info($"RequestQueue initialized (maxConcurrent: {maxConcurrentRequests})");
        }

        /// <summary>
        /// Добавляет запрос в очередь
        /// </summary>
        /// <param name="action">Действие для выполнения</param>
        /// <param name="priority">Приоритет запроса</param>
        public void Enqueue(Func<Task> action, RequestPriority priority = RequestPriority.Normal)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (!_isRunning)
            {
                Logger.Warn("RequestQueue is not running. Request will be queued but not processed until Start() is called.");
            }

            var request = new QueuedRequest
            {
                Action = action,
                Priority = priority,
                EnqueuedAt = DateTime.UtcNow
            };

            lock (_lockObject)
            {
                _priorityQueues[(int)priority].Enqueue(request);
                Logger.Debug($"Request enqueued (priority: {priority}, queue size: {GetTotalQueueSize()})");
            }
        }

        /// <summary>
        /// Запускает обработку очереди
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                Logger.Warn("RequestQueue is already running");
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _isRunning = true;
            _processingTask = Task.Run(() => ProcessQueueAsync(_cancellationTokenSource.Token));
            
            Logger.Info("RequestQueue started");
        }

        /// <summary>
        /// Останавливает обработку очереди
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning)
            {
                Logger.Warn("RequestQueue is not running");
                return;
            }

            Logger.Info("Stopping RequestQueue...");
            
            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            if (_processingTask != null)
            {
                await _processingTask;
            }

            Logger.Info("RequestQueue stopped");
        }

        /// <summary>
        /// Основной цикл обработки очереди
        /// </summary>
        private async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            Logger.Debug("RequestQueue processing loop started");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var request = DequeueHighestPriority();
                    
                    if (request == null)
                    {
                        // Очередь пуста, ждем немного
                        await Task.Delay(100, cancellationToken);
                        continue;
                    }

                    // Ждем доступности токена (rate limiting)
                    await _rateLimiter.WaitForTokenAsync(cancellationToken);

                    // Ждем доступности слота для одновременного выполнения
                    await _concurrencySemaphore.WaitAsync(cancellationToken);

                    // Выполняем запрос асинхронно (не блокируем очередь)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var waitTime = DateTime.UtcNow - request.EnqueuedAt;
                            Logger.Debug($"Processing request (priority: {request.Priority}, waited: {waitTime.TotalMilliseconds:F0}ms)");
                            
                            await request.Action();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"Error processing request (priority: {request.Priority})");
                        }
                        finally
                        {
                            _concurrencySemaphore.Release();
                        }
                    }, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Нормальная отмена при остановке
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Unexpected error in RequestQueue processing loop");
                    await Task.Delay(1000, cancellationToken); // Пауза перед повтором
                }
            }

            Logger.Debug("RequestQueue processing loop stopped");
        }

        /// <summary>
        /// Извлекает запрос с наивысшим приоритетом из очереди
        /// </summary>
        private QueuedRequest? DequeueHighestPriority()
        {
            lock (_lockObject)
            {
                foreach (var kvp in _priorityQueues)
                {
                    var queue = kvp.Value;
                    if (queue.Count > 0)
                    {
                        return queue.Dequeue();
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Получает общее количество запросов в очереди
        /// </summary>
        public int GetTotalQueueSize()
        {
            lock (_lockObject)
            {
                int total = 0;
                foreach (var queue in _priorityQueues.Values)
                {
                    total += queue.Count;
                }
                return total;
            }
        }

        /// <summary>
        /// Получает количество запросов для указанного приоритета
        /// </summary>
        public int GetQueueSize(RequestPriority priority)
        {
            lock (_lockObject)
            {
                return _priorityQueues[(int)priority].Count;
            }
        }

        /// <summary>
        /// Представляет запрос в очереди
        /// </summary>
        private class QueuedRequest
        {
            public Func<Task> Action { get; set; } = null!;
            public RequestPriority Priority { get; set; }
            public DateTime EnqueuedAt { get; set; }
        }
    }
}
