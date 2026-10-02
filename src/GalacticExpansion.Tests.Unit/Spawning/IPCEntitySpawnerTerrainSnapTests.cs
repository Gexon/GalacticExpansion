using System.Text;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.IPC;
using GalacticExpansion.Core.Placement;
using GalacticExpansion.Core.Spawning;
using GalacticExpansion.Models;
using Moq;
using Newtonsoft.Json;
using NLog;
using Xunit;

namespace GalacticExpansion.Tests.Unit.Spawning
{
    /// <summary>
    /// Unit-тесты флага привязки структуры к рельефу в IPC-запросе.
    /// Уровень: Unit, правило 2.1 из docs/architecture/09_Testing_Strategy.md.
    /// Спавн на рельеф ставит SnapToTerrain. Абсолютный SpawnStructureAsync — нет.
    /// </summary>
    public class IPCEntitySpawnerTerrainSnapTests
    {
        private const string ChannelId = "GalacticExpansion";
        private const string Playfield = "Temperate Planet";

        [Fact(DisplayName = "SpawnStructureAtTerrainAsync — запрос просит PfServer посчитать высоту земли")]
        public async Task SpawnStructureAtTerrainAsync_SetsSnapToTerrain_AndKeepsCallerOffset()
        {
            var spawner = CreateDediSpawner(out var channel, out var packetCallback, out var placement);
            SpawnStructureRequest? sent = null;

            channel.Setup(c => c.SendToPlayfieldServer(Playfield, It.IsAny<byte[]>()))
                .Callback<string, byte[]>((_, data) =>
                {
                    sent = JsonConvert.DeserializeObject<SpawnStructureRequest>(Encoding.UTF8.GetString(data));
                    Reply(packetCallback, sent!);
                })
                .Returns(true);

            var entityId = await spawner.SpawnStructureAtTerrainAsync(
                Playfield,
                "BA_ZiraxOutpost",
                x: 120f,
                z: -40f,
                factionId: 2,
                heightOffset: 0.5f);

            Assert.Equal(77, entityId);
            Assert.NotNull(sent);
            Assert.True(sent!.SnapToTerrain);
            Assert.Equal(0.5f, sent.HeightOffset);
            Assert.Equal(120f, sent.Position[0]);
            Assert.Equal(-40f, sent.Position[2]);
            placement.Verify(
                p => p.FindLocationAtTerrainAsync(It.IsAny<string>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
                Times.Never);
        }

        [Fact(DisplayName = "SpawnStructureAsync — абсолютная позиция не привязывается к рельефу")]
        public async Task SpawnStructureAsync_DoesNotSnapToTerrain()
        {
            var spawner = CreateDediSpawner(out var channel, out var packetCallback, out _);
            SpawnStructureRequest? sent = null;

            channel.Setup(c => c.SendToPlayfieldServer(Playfield, It.IsAny<byte[]>()))
                .Callback<string, byte[]>((_, data) =>
                {
                    sent = JsonConvert.DeserializeObject<SpawnStructureRequest>(Encoding.UTF8.GetString(data));
                    Reply(packetCallback, sent!);
                })
                .Returns(true);

            await spawner.SpawnStructureAsync(
                Playfield,
                "BA_ZiraxOutpost",
                new Vector3(10f, 80f, 30f),
                new Vector3(0f, 90f, 0f),
                factionId: 2);

            Assert.NotNull(sent);
            Assert.False(sent!.SnapToTerrain);
            Assert.Equal(80f, sent.Position[1]);
        }

        /// <summary>
        /// Собирает спавнер dedicated и подписывает мост на ответы PfServer.
        /// </summary>
        private static IPCEntitySpawner CreateDediSpawner(
            out Mock<IEmpyrionModChannel> channel,
            out ModDataReceivedDelegate packetCallback,
            out Mock<IPlacementResolver> placement)
        {
            channel = new Mock<IEmpyrionModChannel>();
            channel.Setup(c => c.ChannelId).Returns(ChannelId);

            ModDataReceivedDelegate? captured = null;
            channel.Setup(c => c.RegisterReceiverForPlayfieldPackets(It.IsAny<ModDataReceivedDelegate>()))
                .Callback<ModDataReceivedDelegate>(cb => captured = cb)
                .Returns(true);

            var bridge = new NetworkBridge(channel.Object, new Mock<ILogger>().Object);
            bridge.InitializeForDedi();
            Assert.NotNull(captured);
            packetCallback = captured!;

            placement = new Mock<IPlacementResolver>();
            return new IPCEntitySpawner(
                bridge,
                placement.Object,
                ApplicationMode.DedicatedServer,
                new Mock<ILogger>().Object);
        }

        /// <summary>
        /// Отвечает на запрос так, будто PfServer успешно заспавнил структуру.
        /// </summary>
        private static void Reply(ModDataReceivedDelegate packetCallback, SpawnStructureRequest request)
        {
            var response = new SpawnStructureResponse
            {
                RequestId = request.RequestId,
                Success = true,
                EntityId = 77,
                Playfield = request.Playfield
            };
            packetCallback(ChannelId, request.Playfield, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)));
        }
    }
}
