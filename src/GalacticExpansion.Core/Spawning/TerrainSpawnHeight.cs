namespace GalacticExpansion.Core.Spawning
{
    /// <summary>
    /// Считает высоту, на которой структура должна появиться над землёй.
    /// Формула из Module_04 §3.2 и Module_06 §4: координата Y = высота рельефа + отступ.
    /// Класс отдельно от игры, чтобы формулу можно было проверить unit-тестом.
    /// </summary>
    public static class TerrainSpawnHeight
    {
        /// <summary>
        /// Возвращает Y спавна.
        /// </summary>
        /// <param name="terrainHeight">Высота поверхности земли в метрах. Её даёт IPlayfield.GetTerrainHeightAt.</param>
        /// <param name="heightOffset">На сколько метров поднять структуру над землёй. Для базы это 0.5.</param>
        /// <returns>Итоговая координата Y.</returns>
        public static float Resolve(float terrainHeight, float heightOffset)
        {
            return terrainHeight + heightOffset;
        }
    }
}
