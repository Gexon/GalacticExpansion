using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace GalacticExpansion.Core.Gateway
{
    /// <summary>
    /// Реализует Rate Limiting с использованием алгоритма Token Bucket.
    /// Ограничивает количество запросов к ModAPI для предотвращения перегрузки.
    /// 
    /// Принцип работы Token Bucket:
    /// - Ведро имеет фиксированную вместимость токенов
    /// - Токены пополняются с постоянной скоростью
    /// - Каждый запрос потребляет 1 токен
    /// - Если токенов нет, запрос ожидает пополнения
    /// 
    /// Thread-safe: может использоваться из нескольких потоков.
    /// </summary>
    public class RateLimiter
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        private readonly int _maxTokens;
        private readonly int _refillRate; // токенов в секунду
        private readonly SemaphoreSlim _semaphore;
        
        private float _availableTokens;
        private DateTime _lastRefillTime;
        private readonly object _lockObject = new object();

        /// <summary>
        /// Конструктор Rate Limiter
        /// </summary>
        /// <param name="maxRequestsPerSecond">Максимальное количество запросов в секунду</param>
        public RateLimiter(int maxRequestsPerSecond = 10)
        {
            if (maxRequestsPerSecond <= 0)
                throw new ArgumentException("maxRequestsPerSecond must be positive", nameof(maxRequestsPerSecond));

            _maxTokens = maxRequestsPerSecond;
            _refillRate = maxRequestsPerSecond;
            _availableTokens = maxRequestsPerSecond; // Начинаем с полного ведра
            _lastRefillTime = DateTime.UtcNow;
            _semaphore = new SemaphoreSlim(1, 1);

            Logger.Info($"RateLimiter initialized: {maxRequestsPerSecond} requests/second");
        }

        /// <summary>
        /// Ожидает доступности токена для выполнения запроса.
        /// Блокирует выполнение, если токены закончились.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Task, который завершится, когда токен станет доступен</returns>
        public async Task WaitForTokenAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                while (true)
                {
                    lock (_lockObject)
                    {
                        RefillTokens();

                        if (_availableTokens >= 1.0f)
                        {
                            _availableTokens -= 1.0f;
                            Logger.Trace($"Token consumed. Available: {_availableTokens:F2}/{_maxTokens}");
                            return;
                        }
                    }

                    // Токенов нет, вычисляем время ожидания до следующего пополнения
                    var tokensNeeded = 1.0f - _availableTokens;
                    var waitTimeMs = (int)Math.Ceiling((tokensNeeded / _refillRate) * 1000);
                    
                    Logger.Debug($"Rate limit reached. Waiting {waitTimeMs}ms for token refill...");
                    
                    await Task.Delay(waitTimeMs, cancellationToken);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Пытается получить токен без ожидания.
        /// </summary>
        /// <returns>true, если токен доступен; false, если нет</returns>
        public bool TryAcquireToken()
        {
            lock (_lockObject)
            {
                RefillTokens();

                if (_availableTokens >= 1.0f)
                {
                    _availableTokens -= 1.0f;
                    Logger.Trace($"Token consumed (non-blocking). Available: {_availableTokens:F2}/{_maxTokens}");
                    return true;
                }

                Logger.Debug($"Rate limit reached (non-blocking). Available tokens: {_availableTokens:F2}");
                return false;
            }
        }

        /// <summary>
        /// Пополняет токены на основе прошедшего времени.
        /// Вызывается перед каждой проверкой доступности токенов.
        /// </summary>
        private void RefillTokens()
        {
            var now = DateTime.UtcNow;
            var timeSinceLastRefill = (now - _lastRefillTime).TotalSeconds;

            if (timeSinceLastRefill > 0)
            {
                var tokensToAdd = (float)(timeSinceLastRefill * _refillRate);
                _availableTokens = Math.Min(_availableTokens + tokensToAdd, _maxTokens);
                _lastRefillTime = now;

                if (tokensToAdd > 0.1f) // Логируем только значимые пополнения
                {
                    Logger.Trace($"Tokens refilled: +{tokensToAdd:F2}. Available: {_availableTokens:F2}/{_maxTokens}");
                }
            }
        }

        /// <summary>
        /// Получает текущее количество доступных токенов
        /// </summary>
        public float AvailableTokens
        {
            get
            {
                lock (_lockObject)
                {
                    RefillTokens();
                    return _availableTokens;
                }
            }
        }

        /// <summary>
        /// Сбрасывает rate limiter (восстанавливает все токены)
        /// </summary>
        public void Reset()
        {
            lock (_lockObject)
            {
                _availableTokens = _maxTokens;
                _lastRefillTime = DateTime.UtcNow;
                Logger.Debug("RateLimiter reset");
            }
        }
    }
}
