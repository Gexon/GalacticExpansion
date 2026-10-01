using System;
using System.Runtime.CompilerServices;
using Eleon.Modding;
using GalacticExpansion.Core.IPC;

namespace GalacticExpansion
{
    /// <summary>
    /// Единственное место, откуда мод вызывает ModApi.Network.
    /// Класс лежит в сборке GalacticExpansion.dll. Empyrion запоминает получателя
    /// как имя этой сборки (GetCallingAssembly), поэтому receiver в Send* — то же имя.
    /// Методы помечены NoInlining: если JIT перенесёт вызов в GalacticExpansion.Core,
    /// игра запишет колбэк под чужим именем и пакеты перестанут доходить.
    /// </summary>
    public sealed class EmpyrionModChannel : IEmpyrionModChannel
    {
        /// <summary>
        /// Простое имя этой сборки. Его же игра увидит как ключ колбэка.
        /// </summary>
        public static readonly string ChannelName = typeof(EmpyrionModChannel).Assembly.GetName().Name
            ?? throw new InvalidOperationException("У сборки мода нет простого имени");

        private readonly INetwork _network;

        /// <summary>
        /// Сохраняет сетевой интерфейс процесса, в котором загружен мод.
        /// </summary>
        /// <param name="network">ModApi.Network текущего процесса (Dedi или PfServer)</param>
        public EmpyrionModChannel(INetwork network)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
        }

        /// <inheritdoc />
        public string ChannelId => ChannelName;

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool RegisterReceiverForPlayfieldPackets(ModDataReceivedDelegate callback)
        {
            return RegisterPlayfield(callback);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool RegisterReceiverForDediPackets(ModDataReceivedDelegate callback)
        {
            return RegisterDedi(callback);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool SendToDedicatedServer(byte[] data, string playfieldName)
        {
            return SendDedi(data, playfieldName);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool SendToPlayfieldServer(string playfieldName, byte[] data)
        {
            return SendPlayfield(playfieldName, data);
        }

        /// <summary>
        /// Отдельный кадр стека в этой сборке. Даже если игровой метод заинлайнят сюда,
        /// GetCallingAssembly() всё ещё видит GalacticExpansion, а не Core.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool RegisterPlayfield(ModDataReceivedDelegate callback)
        {
            return _network.RegisterReceiverForPlayfieldPackets(callback);
        }

        /// <summary>
        /// См. <see cref="RegisterPlayfield"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool RegisterDedi(ModDataReceivedDelegate callback)
        {
            return _network.RegisterReceiverForDediPackets(callback);
        }

        /// <summary>
        /// См. <see cref="RegisterPlayfield"/>. Receiver равен имени этой сборки.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool SendDedi(byte[] data, string playfieldName)
        {
            return _network.SendToDedicatedServer(ChannelName, data, playfieldName);
        }

        /// <summary>
        /// См. <see cref="RegisterPlayfield"/>. Receiver равен имени этой сборки.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool SendPlayfield(string playfieldName, byte[] data)
        {
            return _network.SendToPlayfieldServer(ChannelName, playfieldName, data);
        }
    }
}
