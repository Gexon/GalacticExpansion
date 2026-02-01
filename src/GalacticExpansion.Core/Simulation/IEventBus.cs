using System;

namespace GalacticExpansion.Core.Simulation
{
    /// <summary>
    /// Внутренняя шина событий для слабой связи модулей.
    /// Обеспечивает publish-subscribe паттерн для коммуникации между модулями симуляции.
    /// </summary>
    public interface IEventBus
    {
        /// <summary>
        /// Подписаться на события определенного типа.
        /// </summary>
        /// <typeparam name="TEvent">Тип события (используется как ключ)</typeparam>
        /// <param name="handler">Обработчик события</param>
        void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;

        /// <summary>
        /// Отписаться от событий определенного типа.
        /// </summary>
        /// <typeparam name="TEvent">Тип события</typeparam>
        /// <param name="handler">Обработчик для удаления</param>
        void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class;

        /// <summary>
        /// Опубликовать событие для всех подписчиков.
        /// Вызов синхронный - все обработчики выполняются последовательно.
        /// </summary>
        /// <typeparam name="TEvent">Тип события</typeparam>
        /// <param name="eventData">Данные события</param>
        void Publish<TEvent>(TEvent eventData) where TEvent : class;
    }
}
