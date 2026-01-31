using System;
using System.Threading.Tasks;
using Eleon.Modding;

namespace GalacticExpansion.Core.Gateway
{
    /// <summary>
    /// Шлюз для взаимодействия с Empyrion ModAPI.
    /// Предоставляет удобный async/await интерфейс для отправки запросов к игре
    /// и обработки событий. Изолирует всю систему от прямого взаимодействия с ModAPI.
    /// 
    /// Основные функции:
    /// - Асинхронная отправка запросов с автоматическим сопоставлением ответов
    /// - Обработка событий от игры (структуры, игроки, NPC)
    /// - Управление очередью запросов и rate limiting
    /// - Retry логика при ошибках
    /// - Circuit breaker для защиты от перегрузки ModAPI
    /// </summary>
    public interface IEmpyrionGateway
    {
        /// <summary>
        /// Отправляет асинхронный запрос к ModAPI и ожидает ответ.
        /// </summary>
        /// <typeparam name="TResponse">Тип ожидаемого ответа</typeparam>
        /// <param name="requestId">Тип запроса (CmdId)</param>
        /// <param name="data">Данные запроса</param>
        /// <param name="timeoutMs">Таймаут ожидания ответа в миллисекундах</param>
        /// <returns>Ответ от ModAPI</returns>
        /// <exception cref="TimeoutException">Если ответ не получен в течение таймаута</exception>
        /// <exception cref="InvalidOperationException">Если Gateway не запущен</exception>
        Task<TResponse> SendRequestAsync<TResponse>(CmdId requestId, object? data = null, int timeoutMs = 5000);

        /// <summary>
        /// Событие получения данных от игры.
        /// Все события ModAPI проходят через этот обработчик.
        /// </summary>
        event EventHandler<GameEventArgs> GameEventReceived;

        /// <summary>
        /// Обрабатывает событие от ModAPI.
        /// Вызывается из ModInterface.Game_Event().
        /// </summary>
        void HandleEvent(CmdId eventId, ushort seqNr, object data);

        /// <summary>
        /// Запускает Gateway и начинает обработку запросов и событий.
        /// Должен быть вызван в ModInterface.Init().
        /// </summary>
        void Start();

        /// <summary>
        /// Останавливает Gateway, завершает текущие запросы и освобождает ресурсы.
        /// Должен быть вызван в ModInterface.Shutdown().
        /// </summary>
        void Stop();

        /// <summary>
        /// Проверяет, запущен ли Gateway
        /// </summary>
        bool IsRunning { get; }
    }

    /// <summary>
    /// Аргументы события получения данных от игры
    /// </summary>
    public class GameEventArgs : EventArgs
    {
        /// <summary>
        /// Тип события
        /// </summary>
        public CmdId EventId { get; set; }

        /// <summary>
        /// Sequence number (для сопоставления с запросами)
        /// </summary>
        public ushort SequenceNumber { get; set; }

        /// <summary>
        /// Данные события
        /// </summary>
        public object Data { get; set; }

        /// <summary>
        /// Конструктор
        /// </summary>
        public GameEventArgs(CmdId eventId, ushort seqNr, object data)
        {
            EventId = eventId;
            SequenceNumber = seqNr;
            Data = data;
        }
    }

    /// <summary>
    /// Приоритет запроса в очереди.
    /// Меньшее значение = выше приоритет.
    /// </summary>
    public enum RequestPriority
    {
        /// <summary>
        /// Критический приоритет (спавн/удаление структур)
        /// </summary>
        Critical = 0,

        /// <summary>
        /// Высокий приоритет (получение информации о структурах)
        /// </summary>
        High = 1,

        /// <summary>
        /// Нормальный приоритет (обычные запросы)
        /// </summary>
        Normal = 2,

        /// <summary>
        /// Низкий приоритет (фоновые операции)
        /// </summary>
        Low = 3
    }
}
