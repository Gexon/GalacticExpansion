using GalacticExpansion.Core.Spawning;
using Xunit;

namespace GalacticExpansion.Tests.Unit.Spawning
{
    /// <summary>
    /// Unit-тесты формулы высоты спавна.
    /// Уровень: Unit, правило 2.1 из docs/architecture/09_Testing_Strategy.md.
    /// Формула из Module_04 §3.2 и Module_06 §4: Y = высота рельефа + отступ.
    /// </summary>
    public class TerrainSpawnHeightTests
    {
        [Theory(DisplayName = "Resolve — Y равен высоте рельефа плюс отступ")]
        [InlineData(0f, 0.5f, 0.5f)]
        [InlineData(42f, 0.5f, 42.5f)]
        [InlineData(12.25f, 1f, 13.25f)]
        public void Resolve_AddsOffsetToTerrainHeight(float terrainHeight, float heightOffset, float expectedY)
        {
            var y = TerrainSpawnHeight.Resolve(terrainHeight, heightOffset);

            Assert.Equal(expectedY, y);
        }
    }
}
