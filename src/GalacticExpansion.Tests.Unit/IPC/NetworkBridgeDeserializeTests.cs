using System;
using System.Text;
using System.Threading.Tasks;
using Eleon.Modding;
using GalacticExpansion.Core.IPC;
using Moq;
using Newtonsoft.Json;
using NLog;
using Xunit;

namespace GalacticExpansion.Tests.Unit.IPC
{
    /// <summary>
    /// Unit-тесты разбора IPC JSON (уровень 2.1 из docs/architecture/09_Testing_Strategy.md).
    /// Лог v5: DeserializeObject на абстрактном IPCMessage падает на Path 'pf'.
    /// Эти тесты гоняют тот же JSON, который пишет SerializeObject: первым полем идёт pf.
    /// </summary>
    public class NetworkBridgeDeserializeTests
    {
        private const string ChannelId = "GalacticExpansion";
        private const string Playfield = "Temperate Planet";

        /// <summary>
        /// PfServer получает SpawnStructure, у которого JSON начинается с pf, и отдаёт конкретный запрос обработчику.
        /// </summary>
        [Fact]
        public async Task PlayfieldPacket_SpawnStructure_DeliversConcreteRequest()
        {
            var callback = CaptureDediPacketsCallback(out var bridge);
            SpawnStructureRequest? received = null;
            var done = new TaskCompletionSource<bool>();

            bridge.OnRequestReceived += (message, playfield) =>
            {
                received = message as SpawnStructureRequest;
                done.TrySetResult(true);
                return Task.FromResult<IPCMessage?>(null);
            };

            var request = new SpawnStructureRequest
            {
                RequestId = Guid.NewGuid(),
                Playfield = Playfield,
                PrefabName = "BA_ConstructionSite",
                Position = new[] { 10f, 20f, 30f },
                Rotation = new[] { 0f, 90f, 0f },
                FactionId = 2,
                EntityType = 2
            };
            var json = JsonConvert.SerializeObject(request);
            Assert.StartsWith("{\"pf\":", json);

            callback(ChannelId, Playfield, Encoding.UTF8.GetBytes(json));

            var completed = await Task.WhenAny(done.Task, Task.Delay(2000));
            Assert.Same(done.Task, completed);
            Assert.NotNull(received);
            Assert.Equal(request.RequestId, received!.RequestId);
            Assert.Equal("BA_ConstructionSite", received.PrefabName);
            Assert.Equal(Playfield, received.Playfield);
            Assert.Equal(2, received.FactionId);
        }

        /// <summary>
        /// Dedi разбирает PlayfieldReadyNotification (тоже начинается с pf) и поднимает событие готовности.
        /// </summary>
        [Fact]
        public void DediPacket_PlayfieldReadyNotification_RaisesReadyEvent()
        {
            var callback = CapturePlayfieldPacketsCallback(out var bridge);
            string? readyPlayfield = null;
            bridge.OnPlayfieldReadyReceived += playfield => readyPlayfield = playfield;

            var notification = new PlayfieldReadyNotification
            {
                RequestId = Guid.NewGuid(),
                Playfield = Playfield
            };
            var json = JsonConvert.SerializeObject(notification);
            Assert.StartsWith("{\"pf\":", json);

            callback(ChannelId, Playfield, Encoding.UTF8.GetBytes(json));

            Assert.Equal(Playfield, readyPlayfield);
        }

        /// <summary>
        /// Ответ PfServer (первое поле ok, не pf) завершает ожидающий запрос Dedi.
        /// </summary>
        [Fact]
        public async Task DediPacket_SpawnStructureResponse_CompletesPendingRequest()
        {
            var callback = CapturePlayfieldPacketsCallback(out var bridge, out var channel);
            channel.Setup(c => c.SendToPlayfieldServer(Playfield, It.IsAny<byte[]>())).Returns(true);

            var requestId = Guid.NewGuid();
            var pending = bridge.SendRequestToPlayfieldAsync<SpawnStructureResponse>(
                new SpawnStructureRequest
                {
                    RequestId = requestId,
                    Playfield = Playfield,
                    PrefabName = "BA_ConstructionSite"
                },
                Playfield,
                timeoutMs: 2000);

            var response = new SpawnStructureResponse
            {
                RequestId = requestId,
                Success = true,
                EntityId = 42,
                Playfield = Playfield
            };
            callback(ChannelId, Playfield, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)));

            var result = await pending;
            Assert.True(result.Success);
            Assert.Equal(42, result.EntityId);
            Assert.Equal(Playfield, result.Playfield);
        }

        /// <summary>
        /// PfServer получает EntityExists (JSON начинается с pf) и отдаёт конкретный запрос, а не базовый IPCMessage.
        /// </summary>
        [Fact(DisplayName = "EntityExists — JSON доходит до обработчика конкретным типом")]
        public async Task PlayfieldPacket_EntityExists_DeliversConcreteRequest()
        {
            var callback = CaptureDediPacketsCallback(out var bridge);
            EntityExistsRequest? received = null;
            var done = new TaskCompletionSource<bool>();

            bridge.OnRequestReceived += (message, playfield) =>
            {
                received = message as EntityExistsRequest;
                done.TrySetResult(true);
                return Task.FromResult<IPCMessage?>(null);
            };

            var request = new EntityExistsRequest
            {
                RequestId = Guid.NewGuid(),
                Playfield = Playfield,
                EntityId = 55
            };
            var json = JsonConvert.SerializeObject(request);
            Assert.StartsWith("{\"pf\":", json);

            callback(ChannelId, Playfield, Encoding.UTF8.GetBytes(json));

            var completed = await Task.WhenAny(done.Task, Task.Delay(2000));
            Assert.Same(done.Task, completed);
            Assert.NotNull(received);
            Assert.Equal(request.RequestId, received!.RequestId);
            Assert.Equal(55, received.EntityId);
            Assert.Equal(Playfield, received.Playfield);
        }

        /// <summary>
        /// PfServer получает DestroyEntity конкретным типом.
        /// </summary>
        [Fact(DisplayName = "DestroyEntity — JSON доходит до обработчика конкретным типом")]
        public async Task PlayfieldPacket_DestroyEntity_DeliversConcreteRequest()
        {
            var callback = CaptureDediPacketsCallback(out var bridge);
            DestroyEntityRequest? received = null;
            var done = new TaskCompletionSource<bool>();

            bridge.OnRequestReceived += (message, playfield) =>
            {
                received = message as DestroyEntityRequest;
                done.TrySetResult(true);
                return Task.FromResult<IPCMessage?>(null);
            };

            var request = new DestroyEntityRequest
            {
                RequestId = Guid.NewGuid(),
                Playfield = Playfield,
                EntityId = 88
            };
            var json = JsonConvert.SerializeObject(request);
            Assert.StartsWith("{\"pf\":", json);

            callback(ChannelId, Playfield, Encoding.UTF8.GetBytes(json));

            var completed = await Task.WhenAny(done.Task, Task.Delay(2000));
            Assert.Same(done.Task, completed);
            Assert.NotNull(received);
            Assert.Equal(request.RequestId, received!.RequestId);
            Assert.Equal(88, received.EntityId);
            Assert.Equal(Playfield, received.Playfield);
        }

        /// <summary>
        /// Неизвестное поле type не должно ронять callback: обработчик не вызывается.
        /// </summary>
        [Fact]
        public async Task PlayfieldPacket_UnknownType_DoesNotInvokeHandler()
        {
            var callback = CaptureDediPacketsCallback(out var bridge);
            var called = false;
            bridge.OnRequestReceived += (message, playfield) =>
            {
                called = true;
                return Task.FromResult<IPCMessage?>(null);
            };

            callback(ChannelId, Playfield, Encoding.UTF8.GetBytes("{\"pf\":\"Temperate Planet\",\"type\":\"NotAMessage\"}"));
            await Task.Delay(50);

            Assert.False(called);
        }

        /// <summary>
        /// Подписка PfServer: пакеты приходят с dedicated, callback — RegisterReceiverForDediPackets.
        /// </summary>
        private static ModDataReceivedDelegate CaptureDediPacketsCallback(out NetworkBridge bridge)
        {
            var channel = new Mock<IEmpyrionModChannel>();
            channel.Setup(c => c.ChannelId).Returns(ChannelId);
            ModDataReceivedDelegate? captured = null;
            channel.Setup(c => c.RegisterReceiverForDediPackets(It.IsAny<ModDataReceivedDelegate>()))
                .Callback<ModDataReceivedDelegate>(cb => captured = cb)
                .Returns(true);

            bridge = new NetworkBridge(channel.Object, new Mock<ILogger>().Object);
            bridge.InitializeForPlayfieldServer(Playfield);
            Assert.NotNull(captured);
            return captured!;
        }

        /// <summary>
        /// Подписка Dedi: пакеты приходят с playfield, callback — RegisterReceiverForPlayfieldPackets.
        /// </summary>
        private static ModDataReceivedDelegate CapturePlayfieldPacketsCallback(out NetworkBridge bridge)
        {
            return CapturePlayfieldPacketsCallback(out bridge, out _);
        }

        private static ModDataReceivedDelegate CapturePlayfieldPacketsCallback(
            out NetworkBridge bridge,
            out Mock<IEmpyrionModChannel> channel)
        {
            channel = new Mock<IEmpyrionModChannel>();
            channel.Setup(c => c.ChannelId).Returns(ChannelId);
            ModDataReceivedDelegate? captured = null;
            channel.Setup(c => c.RegisterReceiverForPlayfieldPackets(It.IsAny<ModDataReceivedDelegate>()))
                .Callback<ModDataReceivedDelegate>(cb => captured = cb)
                .Returns(true);

            bridge = new NetworkBridge(channel.Object, new Mock<ILogger>().Object);
            bridge.InitializeForDedi();
            Assert.NotNull(captured);
            return captured!;
        }
    }
}
