using System.Collections.Generic;
using System.Threading.Tasks;
using GalacticExpansion.Models;

namespace GalacticExpansion.Core.Spawning
{
    /// <summary>
    /// Интерфейс для создания и удаления игровых сущностей (структуры, корабли, NPC).
    /// Предоставляет методы для спавна на рельефе с точным определением высоты (API v1.15+),
    /// спавна группы NPC по кругу и безопасного удаления сущностей.
    /// </summary>
    public interface IEntitySpawner
    {
        /// <summary>
        /// Спавнит структуру (Base/CV/SV/HV) на указанной позиции.
        /// </summary>
        /// <param name="prefabName">Название prefab (например, "GLEX_Base_L1")</param>
        /// <param name="position">Позиция для спавна</param>
        /// <param name="rotation">Поворот структуры (в градусах)</param>
        /// <param name="factionId">ID фракции (обычно 2 для Zirax)</param>
        /// <returns>Entity ID созданной структуры</returns>
        /// <exception cref="SpawnException">Выбрасывается при ошибке спавна</exception>
        Task<int> SpawnStructureAsync(string prefabName, Vector3 position, Vector3 rotation, int factionId);

        /// <summary>
        /// Спавнит структуру на рельефе с точным определением высоты (API v1.15+).
        /// Использует IPlayfield.GetTerrainHeightAt() для корректного размещения.
        /// </summary>
        /// <param name="playfield">Название playfield</param>
        /// <param name="prefabName">Название prefab</param>
        /// <param name="x">X координата</param>
        /// <param name="z">Z координата</param>
        /// <param name="factionId">ID фракции</param>
        /// <param name="heightOffset">Смещение над поверхностью (метры), по умолчанию 0.5м</param>
        /// <returns>Entity ID созданной структуры</returns>
        /// <exception cref="SpawnException">Выбрасывается при ошибке спавна</exception>
        Task<int> SpawnStructureAtTerrainAsync(
            string playfield,
            string prefabName,
            float x,
            float z,
            int factionId,
            float heightOffset = 0.5f
        );

        /// <summary>
        /// Спавнит группу NPC по кругу вокруг указанной позиции.
        /// NPC размещаются на расстоянии 3м друг от друга.
        /// </summary>
        /// <param name="playfield">Название playfield</param>
        /// <param name="npcClassName">Класс NPC (например, "ZiraxMinigunPatrol")</param>
        /// <param name="centerPosition">Центр круга</param>
        /// <param name="count">Количество NPC для спавна</param>
        /// <param name="factionName">Название фракции (например, "Zirax")</param>
        /// <returns>Список Entity ID созданных NPC</returns>
        /// <exception cref="SpawnException">Выбрасывается при ошибке спавна</exception>
        Task<List<int>> SpawnNPCGroupAsync(
            string playfield,
            string npcClassName,
            Vector3 centerPosition,
            int count,
            string factionName
        );

        /// <summary>
        /// Спавнит одного NPC на рельефе с точным определением высоты.
        /// </summary>
        /// <param name="playfield">Название playfield</param>
        /// <param name="npcClassName">Класс NPC</param>
        /// <param name="x">X координата</param>
        /// <param name="z">Z координата</param>
        /// <param name="factionName">Название фракции</param>
        /// <returns>Entity ID созданного NPC</returns>
        /// <exception cref="SpawnException">Выбрасывается при ошибке спавна</exception>
        Task<int> SpawnNPCAtTerrainAsync(
            string playfield,
            string npcClassName,
            float x,
            float z,
            string factionName
        );

        /// <summary>
        /// Удаляет сущность по ID.
        /// Безопасен к повторному вызову (если сущность уже удалена, не выбрасывает исключение).
        /// </summary>
        /// <param name="entityId">ID сущности для удаления</param>
        Task DestroyEntityAsync(int entityId);

        /// <summary>
        /// Удаляет несколько сущностей пакетом.
        /// </summary>
        /// <param name="entityIds">Список ID сущностей для удаления</param>
        /// <returns>Количество успешно удалённых сущностей</returns>
        Task<int> DestroyEntitiesAsync(IEnumerable<int> entityIds);

        /// <summary>
        /// Проверяет, существует ли сущность с указанным ID.
        /// </summary>
        /// <param name="entityId">ID сущности</param>
        /// <returns>true, если сущность существует; false, если удалена или не найдена</returns>
        Task<bool> EntityExistsAsync(int entityId);
    }
}
