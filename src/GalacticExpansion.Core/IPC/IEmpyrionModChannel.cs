using Eleon.Modding;

namespace GalacticExpansion.Core.IPC
{
    /// <summary>
    /// Канал IPC к Empyrion INetwork.
    /// Реализация обязана жить в сборке GalacticExpansion.dll и сама вызывать ModApi.Network.
    /// Игра кладёт колбэк в словарь под Assembly.GetCallingAssembly().GetName().Name,
    /// а Send* ищет получателя по строке receiver. Оба имени должны совпасть.
    /// Вызов из GalacticExpansion.Core.dll регистрирует канал "GalacticExpansion.Core",
    /// и пакет с receiver "GalacticExpansion" до колбэка не доходит.
    /// </summary>
    public interface IEmpyrionModChannel
    {
        /// <summary>
        /// Имя канала. Совпадает с простым именем сборки, из которой уходят Register* и Send*.
        /// </summary>
        string ChannelId { get; }

        /// <summary>
        /// Подписка dedicated-процесса на пакеты, отправленные с playfield.
        /// </summary>
        bool RegisterReceiverForPlayfieldPackets(ModDataReceivedDelegate callback);

        /// <summary>
        /// Подписка playfield-процесса на пакеты, отправленные с dedicated.
        /// </summary>
        bool RegisterReceiverForDediPackets(ModDataReceivedDelegate callback);

        /// <summary>
        /// Пакет playfield → dedicated. Канал подставляет реализация.
        /// </summary>
        bool SendToDedicatedServer(byte[] data, string playfieldName);

        /// <summary>
        /// Пакет dedicated → playfield. Канал подставляет реализация.
        /// </summary>
        bool SendToPlayfieldServer(string playfieldName, byte[] data);
    }
}
