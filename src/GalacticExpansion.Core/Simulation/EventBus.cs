using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NLog;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Thread-safe реализация шины событий для внутренней коммуникации модулей.
    /// Использует ConcurrentDictionary для безопасной работы из разных потоков.
    /// </summary>
    public class EventBus : IEventBus
    {
        private readonly ILogger _logger;
        
        // Словарь: тип события -> список обработчиков
        // ConcurrentDictionary обеспечивает thread-safety
        private readonly ConcurrentDictionary<Type, List<Delegate>> _subscriptions;
        
        // Блокировка для модификации списков подписчиков
        private readonly object _subscriptionLock = new object();

        /// <summary>
        /// Создает новый экземпляр EventBus.
        /// </summary>
        /// <param name="logger">Логгер для отладки и мониторинга</param>
        public EventBus(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _subscriptions = new ConcurrentDictionary<Type, List<Delegate>>();
            
            _logger.Debug("EventBus initialized");
        }

        /// <inheritdoc/>
        public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var eventType = typeof(TEvent);
            
            lock (_subscriptionLock)
            {
                // Получаем или создаем список обработчиков для данного типа события
                var handlers = _subscriptions.GetOrAdd(eventType, _ => new List<Delegate>());
                
                // Добавляем обработчик, если его еще нет
                if (!handlers.Contains(handler))
                {
                    handlers.Add(handler);
                    _logger.Trace($"Subscribed to event {eventType.Name}, total subscribers: {handlers.Count}");
                }
            }
        }

        /// <inheritdoc/>
        public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var eventType = typeof(TEvent);
            
            lock (_subscriptionLock)
            {
                if (_subscriptions.TryGetValue(eventType, out var handlers))
                {
                    handlers.Remove(handler);
                    _logger.Trace($"Unsubscribed from event {eventType.Name}, remaining subscribers: {handlers.Count}");
                    
                    // Удаляем пустой список для экономии памяти
                    if (handlers.Count == 0)
                    {
                        _subscriptions.TryRemove(eventType, out _);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void Publish<TEvent>(TEvent eventData) where TEvent : class
        {
            if (eventData == null)
                throw new ArgumentNullException(nameof(eventData));

            var eventType = typeof(TEvent);
            
            // Получаем копию списка обработчиков для избежания проблем с concurrent modification
            List<Delegate> handlersCopy;
            lock (_subscriptionLock)
            {
                if (!_subscriptions.TryGetValue(eventType, out var handlers) || handlers.Count == 0)
                {
                    _logger.Trace($"No subscribers for event {eventType.Name}");
                    return;
                }
                
                handlersCopy = new List<Delegate>(handlers);
            }
            
            _logger.Trace($"Publishing event {eventType.Name} to {handlersCopy.Count} subscribers");
            
            // Вызываем обработчики синхронно
            foreach (var handler in handlersCopy)
            {
                try
                {
                    // Приводим к типизированному Action и вызываем
                    var typedHandler = (Action<TEvent>)handler;
                    typedHandler(eventData);
                }
                catch (Exception ex)
                {
                    // Изолируем ошибки в обработчиках - один упавший обработчик не должен ломать остальные
                    _logger.Error(ex, $"Error in event handler for {eventType.Name}: {ex.Message}");
                }
            }
        }
    }
}
