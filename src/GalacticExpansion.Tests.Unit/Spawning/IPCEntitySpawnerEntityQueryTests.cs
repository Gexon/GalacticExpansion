using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.Gateway;
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
            var spawner = CreateDediSpawner(out var channel, out var packetCallback, out _);
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
            var spawner = CreateDediSpawner(out var channel, out var packetCallback, out _);

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
            var spawner = CreateDediSpawner(out var channel, out var packetCallback, out _);

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
            var spawner = CreateDediSpawner(out var channel, out _, out _);
            channel.Setup(c => c.SendToPlayfieldServer(Playfield, It.IsAny<byte[]>())).Returns(true);

            var exists = await spawner.EntityExistsAsync(Playfield, 55);

            Assert.False(exists);
        }

        [Fact(DisplayName = "EntityExistsAsync — невалидный id не уходит в IPC")]
        public async Task EntityExistsAsync_InvalidId_DoesNotSend()
        {
            var spawner = CreateDediSpawner(out var channel, out _, out _);

            var exists = await spawner.EntityExistsAsync(Playfield, 0);

            Assert.False(exists);
            channel.Verify(c => c.SendToPlayfieldServer(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
        }

        [Fact(DisplayName = "DestroyEntityAsync — dedicated шлёт DestroyEntity и не падает на ok=false")]
        public async Task DestroyEntityAsync_FailureResponse_DoesNotThrow()
        {
            var spawner = CreateDediSpawner(out var channel, out var packetCallback, out _);
            DestroyEntityRequest? sent = null;

            channel.Setup(c => c.SendToPlayfieldServer(Playfield, It.IsAny<byte[]>()))
                .Callback<string, byte[]>((_, data) =>
                {
                    var json = Encoding.UTF8.GetString(data);
                    if (json.Contains("\"type\":\"EntityExists\""))
                    {
                        var exists = JsonConvert.DeserializeObject<EntityExistsRequest>(json);
                        ReplyExists(packetCallback, exists!, exists: false);
                        return;
                    }

                    sent = JsonConvert.DeserializeObject<DestroyEntityRequest>(json);
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

            var removed = await spawner.DestroyEntityAsync(Playfield, 88);

            Assert.True(removed);
            Assert.NotNull(sent);
            Assert.Equal("DestroyEntity", sent!.MessageType);
            Assert.Equal(88, sent.EntityId);
        }

        [Fact(DisplayName = "DestroyEntityAsync — сначала Request_Entity_Destroy, затем IPC DestroyEntity")]
        public async Task DestroyEntityAsync_SendsStructureDestroy_BeforePlayfieldRemove()
        {
            var spawner = CreateDediSpawner(out var channel, out var packetCallback, out var gateway);
            var steps = new List<string>();

            gateway.Setup(g => g.SendRequestAsync<object>(
                    CmdId.Request_Entity_Destroy,
                    It.Is<Id>(id => id.id == 88),
                    It.IsAny<int>()))
                .Callback(() => steps.Add("Request_Entity_Destroy"))
                .ReturnsAsync(new object());

            channel.Setup(c => c.SendToPlayfieldServer(Playfield, It.IsAny<byte[]>()))
                .Callback<string, byte[]>((_, data) =>
                {
                    var json = Encoding.UTF8.GetString(data);
                    if (json.Contains("\"type\":\"EntityExists\""))
                    {
                        steps.Add("EntityExists");
                        var exists = JsonConvert.DeserializeObject<EntityExistsRequest>(json);
                        ReplyExists(packetCallback, exists!, exists: false);
                        return;
                    }

                    steps.Add("DestroyEntity");
                    var sent = JsonConvert.DeserializeObject<DestroyEntityRequest>(json);
                    var response = new DestroyEntityResponse
                    {
                        RequestId = sent!.RequestId,
                        Success = true,
                        Playfield = sent.Playfield,
                        EntityId = sent.EntityId
                    };
                    packetCallback(ChannelId, Playfield, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)));
                })
                .Returns(true);

            var removed = await spawner.DestroyEntityAsync(Playfield, 88);

            Assert.True(removed);
            Assert.Equal(new[] { "Request_Entity_Destroy", "DestroyEntity", "EntityExists" }, steps);
        }

        [Fact(DisplayName = "DestroyEntityAsync — false, если структура всё ещё на playfield")]
        public async Task DestroyEntityAsync_ReturnsFalse_WhenStructureStillExists()
        {
            var spawner = CreateDediSpawner(out var channel, out var packetCallback, out _);

            channel.Setup(c => c.SendToPlayfieldServer(Playfield, It.IsAny<byte[]>()))
                .Callback<string, byte[]>((_, data) =>
                {
                    var json = Encoding.UTF8.GetString(data);
                    if (json.Contains("\"type\":\"EntityExists\""))
                    {
                        var exists = JsonConvert.DeserializeObject<EntityExistsRequest>(json);
                        ReplyExists(packetCallback, exists!, exists: true);
                        return;
                    }

                    var sent = JsonConvert.DeserializeObject<DestroyEntityRequest>(json);
                    var response = new DestroyEntityResponse
                    {
                        RequestId = sent!.RequestId,
                        Success = true,
                        Playfield = sent.Playfield,
                        EntityId = sent.EntityId
                    };
                    packetCallback(ChannelId, Playfield, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)));
                })
                .Returns(true);

            var removed = await spawner.DestroyEntityAsync(Playfield, 88);

            Assert.False(removed);
        }

        /// <summary>
        /// Собирает спавнер dedicated и подписывает мост на ответы playfield.
        /// </summary>
        private static IPCEntitySpawner CreateDediSpawner(
            out Mock<IEmpyrionModChannel> channel,
            out ModDataReceivedDelegate packetCallback,
            out Mock<IEmpyrionGateway> gateway)
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
            gateway = new Mock<IEmpyrionGateway>();
            gateway.Setup(g => g.SendRequestAsync<object>(
                    CmdId.Request_Entity_Destroy,
                    It.IsAny<Id>(),
                    It.IsAny<int>()))
                .ReturnsAsync(new object());

            return new IPCEntitySpawner(
                bridge,
                placement.Object,
                gateway.Object,
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
