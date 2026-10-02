using System.Text;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.IPC;
using GalacticExpansion.Core.Placement;
using GalacticExpansion.Core.Spawning;
using Moq;
using Newtonsoft.Json;
using NLog;
using Xunit;

namespace GalacticExpansion.Tests.Unit.Spawning
{
    /// <summary>
    /// Unit-тесты проверки и удаления сущности через IPC с dedicated.
    /// Уровень: Unit, правило 2.1 из docs/architecture/09_Testing_Strategy.md.
    /// Dedicated не смотрит сущности сам: он ждёт ответ playfield и не бросает исключение, если ответа нет.
    /// </summary>
    public class IPCEntitySpawnerEntityQueryTests
    {
        private const string ChannelId = "GalacticExpansion";
        private const string Playfield = "Temperate Planet";

        [Fact(DisplayName = "EntityExistsAsync — dedicated возвращает true, если playfield подтвердил сущность")]
        public async Task EntityExistsAsync_ReturnsTrue_WhenPlayfieldSaysExists()
        {
            var spawner = CreateDediSpawner(out var channel, out var packetCallback);
            EntityExistsRequest? sent = null;

            channel.Setup(c => c.SendToPlayfieldServer(Playfield, It.IsAny<byte[]>()))
                .Callback<string, byte[]>((_, data) =>
                {
                    sent = JsonConvert.DeserializeObject<EntityExistsRequest>(Encoding.UTF8.GetString(data));
                    ReplyExists(packetCallback, sent!, exists: true);
                })
                .Returns(true);

            var exists = await spawner.EntityExistsAsync(Playfield, 55);

            Assert.True(exists);
            Assert.NotNull(sent);
            Assert.Equal("EntityExists", sent!.MessageType);
            Assert.Equal(55, sent.EntityId);
            Assert.Equal(Playfield, sent.Playfield);
        }

        [Fact(DisplayName = "EntityExistsAsync — dedicated возвращает false, если сущности нет")]
        public async Task EntityExistsAsync_ReturnsFalse_WhenPlayfieldSaysMissing()
        {
            var spawner = CreateDediSpawner(out var channel, out var packetCallback);

            channel.Setup(c => c.SendToPlayfieldServer(Playfield, It.IsAny<byte[]>()))
                .Callback<string, byte[]>((_, data) =>
                {
                    var sent = JsonConvert.DeserializeObject<EntityExistsRequest>(Encoding.UTF8.GetString(data));
                    ReplyExists(packetCallback, sent!, exists: false);
                })
                .Returns(true);

            var exists = await spawner.EntityExistsAsync(Playfield, 55);

            Assert.False(exists);
        }

        [Fact(DisplayName = "EntityExistsAsync — ok=false не бросает исключение и даёт false")]
        public async Task EntityExistsAsync_ReturnsFalse_WhenPlayfieldReportsFailure()
        {
            var spawner = CreateDediSpawner(out var channel, out var packetCallback);

            channel.Setup(c => c.SendToPlayfieldServer(Playfield, It.IsAny<byte[]>()))
                .Callback<string, byte[]>((_, data) =>
                {
                    var sent = JsonConvert.DeserializeObject<EntityExistsRequest>(Encoding.UTF8.GetString(data));
                    var response = new EntityExistsResponse
                    {
                        RequestId = sent!.RequestId,
                        Success = false,
                        Exists = false,
                        ErrorMessage = "playfield mismatch",
                        Playfield = sent.Playfield,
                        EntityId = sent.EntityId
                    };
                    packetCallback(ChannelId, Playfield, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)));
                })
                .Returns(true);

            var exists = await spawner.EntityExistsAsync(Playfield, 55);

            Assert.False(exists);
        }

        [Fact(DisplayName = "EntityExistsAsync — таймаут IPC не бросает исключение")]
        public async Task EntityExistsAsync_Timeout_ReturnsFalse()
        {
            var spawner = CreateDediSpawner(out var channel, out _);
            channel.Setup(c => c.SendToPlayfieldServer(Playfield, It.IsAny<byte[]>())).Returns(true);

            var exists = await spawner.EntityExistsAsync(Playfield, 55);

            Assert.False(exists);
        }

        [Fact(DisplayName = "EntityExistsAsync — невалидный id не уходит в IPC")]
        public async Task EntityExistsAsync_InvalidId_DoesNotSend()
        {
            var spawner = CreateDediSpawner(out var channel, out _);

            var exists = await spawner.EntityExistsAsync(Playfield, 0);

            Assert.False(exists);
            channel.Verify(c => c.SendToPlayfieldServer(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
        }

        [Fact(DisplayName = "DestroyEntityAsync — dedicated шлёт DestroyEntity и не падает на ok=false")]
        public async Task DestroyEntityAsync_FailureResponse_DoesNotThrow()
        {
            var spawner = CreateDediSpawner(out var channel, out var packetCallback);
            DestroyEntityRequest? sent = null;

            channel.Setup(c => c.SendToPlayfieldServer(Playfield, It.IsAny<byte[]>()))
                .Callback<string, byte[]>((_, data) =>
                {
                    sent = JsonConvert.DeserializeObject<DestroyEntityRequest>(Encoding.UTF8.GetString(data));
                    var response = new DestroyEntityResponse
                    {
                        RequestId = sent!.RequestId,
                        Success = false,
                        ErrorMessage = "already gone",
                        Playfield = sent.Playfield,
                        EntityId = sent.EntityId
                    };
                    packetCallback(ChannelId, Playfield, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)));
                })
                .Returns(true);

            await spawner.DestroyEntityAsync(Playfield, 88);

            Assert.NotNull(sent);
            Assert.Equal("DestroyEntity", sent!.MessageType);
            Assert.Equal(88, sent.EntityId);
        }

        /// <summary>
        /// Собирает спавнер dedicated и подписывает мост на ответы playfield.
        /// </summary>
        private static IPCEntitySpawner CreateDediSpawner(
            out Mock<IEmpyrionModChannel> channel,
            out ModDataReceivedDelegate packetCallback)
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

            var placement = new Mock<IPlacementResolver>();
            return new IPCEntitySpawner(
                bridge,
                placement.Object,
                ApplicationMode.DedicatedServer,
                new Mock<ILogger>().Object);
        }

        /// <summary>
        /// Отвечает так, будто playfield проверил сущность.
        /// </summary>
        private static void ReplyExists(ModDataReceivedDelegate packetCallback, EntityExistsRequest request, bool exists)
        {
            var response = new EntityExistsResponse
            {
                RequestId = request.RequestId,
                Success = true,
                Exists = exists,
                Playfield = request.Playfield,
                EntityId = request.EntityId
            };
            packetCallback(ChannelId, request.Playfield, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)));
        }
    }
}
