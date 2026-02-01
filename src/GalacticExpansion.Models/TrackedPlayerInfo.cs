using System;

namespace GalacticExpansion.Models
{
    /// <summary>
    /// Информация об отслеживаемом игроке для кэширования его местоположения и статуса.
    /// Используется PlayerTracker для хранения данных об игроках.
    /// Отличается от Eleon.Modding.PlayerInfo тем, что содержит дополнительные поля для отслеживания.
    /// </summary>
    public class TrackedPlayerInfo
    {
        /// <summary>
        /// Уникальный ID игрока (EntityId).
        /// </summary>
        public int PlayerId { get; set; }

        /// <summary>
        /// Имя игрока (SteamName).
        /// </summary>
        public string PlayerName { get; set; }

        /// <summary>
        /// Текущий playfield, на котором находится игрок.
        /// Пустая строка если игрок оффлайн.
        /// </summary>
        public string CurrentPlayfield { get; set; }

        /// <summary>
        /// Позиция игрока на playfield.
        /// </summary>
        public Vector3 Position { get; set; }

        /// <summary>
        /// Время последнего обновления информации (UTC).
        /// </summary>
        public DateTime LastSeen { get; set; }

        /// <summary>
        /// Флаг, показывающий, онлайн ли игрок.
        /// </summary>
        public bool IsOnline { get; set; }

        /// <summary>
        /// Создает новый экземпляр TrackedPlayerInfo.
        /// </summary>
        public TrackedPlayerInfo()
        {
            PlayerName = string.Empty;
            CurrentPlayfield = string.Empty;
            Position = new Vector3();
            LastSeen = DateTime.UtcNow;
            IsOnline = false;
        }
    }
}
