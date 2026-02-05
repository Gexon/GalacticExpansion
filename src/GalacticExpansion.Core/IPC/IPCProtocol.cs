using System;
using Newtonsoft.Json;

namespace GalacticExpansion.Core.IPC
{
    /// <summary>
    /// Базовый класс для всех IPC сообщений между Dedi и PfServer процессами.
    /// Все сообщения содержат версию протокола, тип сообщения и уникальный RequestId для tracking.
    /// </summary>
    public abstract class IPCMessage
    {
        /// <summary>
        /// Версия протокола IPC (для совместимости при обновлениях)
        /// </summary>
        [JsonProperty("v")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Тип сообщения (для десериализации в правильный класс)
        /// </summary>
        [JsonProperty("type")]
        public string MessageType { get; set; } = string.Empty;

        /// <summary>
        /// Уникальный идентификатор запроса для сопоставления request/response
        /// </summary>
        [JsonProperty("reqId")]
        public Guid RequestId { get; set; }

        /// <summary>
        /// Временная метка создания сообщения (UTC)
        /// </summary>
        [JsonProperty("ts")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Запрос от Dedi к PfServer на спавн структуры.
    /// PfServer получает этот запрос, выполняет spawn и отправляет SpawnStructureResponse обратно.
    /// </summary>
    public class SpawnStructureRequest : IPCMessage
    {
        public SpawnStructureRequest()
        {
            MessageType = "SpawnStructure";
        }

        /// <summary>
        /// Название playfield где нужно заспавнить структуру
        /// </summary>
        [JsonProperty("pf")]
        public string Playfield { get; set; } = string.Empty;

        /// <summary>
        /// Имя префаба структуры (без расширения .epb)
        /// </summary>
        [JsonProperty("prefab")]
        public string PrefabName { get; set; } = string.Empty;

        /// <summary>
        /// Позиция спавна (X, Y, Z)
        /// </summary>
        [JsonProperty("pos")]
        public float[] Position { get; set; } = new float[3];

        /// <summary>
        /// Ротация (X, Y, Z)
        /// </summary>
        [JsonProperty("rot")]
        public float[] Rotation { get; set; } = new float[3];

        /// <summary>
        /// ID фракции-владельца (int, не byte!)
        /// </summary>
        [JsonProperty("fac")]
        public int FactionId { get; set; }

        /// <summary>
        /// Тип сущности (BA=2, CV=3, SV=4, HV=5)
        /// </summary>
        [JsonProperty("etype")]
        public byte EntityType { get; set; }
    }

    /// <summary>
    /// Ответ от PfServer к Dedi после попытки спавна.
    /// Содержит результат операции (успех/ошибка) и EntityId если успешно.
    /// </summary>
    public class SpawnStructureResponse : IPCMessage
    {
        public SpawnStructureResponse()
        {
            MessageType = "SpawnStructureResponse";
        }

        /// <summary>
        /// Флаг успешности операции
        /// </summary>
        [JsonProperty("ok")]
        public bool Success { get; set; }

        /// <summary>
        /// ID заспавненной структуры (если Success = true)
        /// </summary>
        [JsonProperty("eid")]
        public int EntityId { get; set; }

        /// <summary>
        /// Текст ошибки (если Success = false)
        /// </summary>
        [JsonProperty("err")]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Название playfield где произошел spawn (для проверки)
        /// </summary>
        [JsonProperty("pf")]
        public string Playfield { get; set; } = string.Empty;
    }

    /// <summary>
    /// Запрос от Dedi к PfServer на проверку готовности playfield для spawn операций.
    /// Используется перед материализацией для убедительности что playfield полностью загружен.
    /// </summary>
    public class PlayfieldReadyRequest : IPCMessage
    {
        public PlayfieldReadyRequest()
        {
            MessageType = "PlayfieldReady";
        }

        /// <summary>
        /// Название playfield для проверки
        /// </summary>
        [JsonProperty("pf")]
        public string Playfield { get; set; } = string.Empty;
    }

    /// <summary>
    /// Ответ от PfServer к Dedi с информацией о готовности playfield.
    /// </summary>
    public class PlayfieldReadyResponse : IPCMessage
    {
        public PlayfieldReadyResponse()
        {
            MessageType = "PlayfieldReadyResponse";
        }

        /// <summary>
        /// Готов ли playfield для spawn операций
        /// </summary>
        [JsonProperty("ready")]
        public bool IsReady { get; set; }

        /// <summary>
        /// Название playfield
        /// </summary>
        [JsonProperty("pf")]
        public string Playfield { get; set; } = string.Empty;

        /// <summary>
        /// Дополнительная информация (если не готов)
        /// </summary>
        [JsonProperty("info")]
        public string? Info { get; set; }
    }

    /// <summary>
    /// Запрос от Dedi к PfServer на спавн NPC.
    /// PfServer получает этот запрос, выполняет spawn и отправляет SpawnNPCResponse обратно.
    /// </summary>
    public class SpawnNPCRequest : IPCMessage
    {
        public SpawnNPCRequest()
        {
            MessageType = "SpawnNPC";
        }

        /// <summary>
        /// Название playfield где нужно заспавнить NPC
        /// </summary>
        [JsonProperty("pf")]
        public string Playfield { get; set; } = string.Empty;

        /// <summary>
        /// Класс NPC (например 'ZiraxMale', 'AlienCivilian')
        /// </summary>
        [JsonProperty("npcClass")]
        public string NPCClassName { get; set; } = string.Empty;

        /// <summary>
        /// Позиция спавна (X, Y, Z)
        /// </summary>
        [JsonProperty("pos")]
        public float[] Position { get; set; } = new float[3];

        /// <summary>
        /// Ротация (X, Y, Z) - обычно только Y (yaw) для NPC
        /// </summary>
        [JsonProperty("rot")]
        public float[] Rotation { get; set; } = new float[3];

        /// <summary>
        /// Название фракции (string, т.к. у нас пока нет mapping к int factionId для NPC)
        /// </summary>
        [JsonProperty("faction")]
        public string FactionName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Ответ от PfServer к Dedi после попытки спавна NPC.
    /// Содержит результат операции (успех/ошибка) и EntityId если успешно.
    /// </summary>
    public class SpawnNPCResponse : IPCMessage
    {
        public SpawnNPCResponse()
        {
            MessageType = "SpawnNPCResponse";
        }

        /// <summary>
        /// Флаг успешности операции
        /// </summary>
        [JsonProperty("ok")]
        public bool Success { get; set; }

        /// <summary>
        /// ID заспавненного NPC (если Success = true)
        /// </summary>
        [JsonProperty("eid")]
        public int EntityId { get; set; }

        /// <summary>
        /// Текст ошибки (если Success = false)
        /// </summary>
        [JsonProperty("err")]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Название playfield где произошел spawn (для проверки)
        /// </summary>
        [JsonProperty("pf")]
        public string Playfield { get; set; } = string.Empty;
    }
}
