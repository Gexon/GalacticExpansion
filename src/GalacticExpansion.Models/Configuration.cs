using System.Collections.Generic;
using Newtonsoft.Json;

namespace GalacticExpansion.Models
{
    /// <summary>
    /// Корневая конфигурация мода GalacticExpansion.
    /// Загружается из Configuration.json и содержит все настройки баланса,
    /// лимиты, prefab mappings и другие параметры.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class Configuration
    {
        /// <summary>
        /// Версия конфигурации
        /// </summary>
        [JsonProperty("Version")]
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// Уровень логирования (Trace, Debug, Information, Warning, Error)
        /// </summary>
        [JsonProperty("LogLevel")]
        public string LogLevel { get; set; } = "Information";

        /// <summary>
        /// Настройки симуляции
        /// </summary>
        [JsonProperty("Simulation")]
        public SimulationSettings Simulation { get; set; } = new SimulationSettings();

        /// <summary>
        /// Домашняя планета для первой колонии
        /// </summary>
        [JsonProperty("HomePlayfield")]
        public string HomePlayfield { get; set; } = "Akua";

        /// <summary>
        /// Включить экспансию на другие планеты
        /// </summary>
        [JsonProperty("EnableExpansion")]
        public bool EnableExpansion { get; set; } = false;

        /// <summary>
        /// Лимиты системы
        /// </summary>
        [JsonProperty("Limits")]
        public LimitsSettings Limits { get; set; } = new LimitsSettings();

        /// <summary>
        /// Настройки фракции Zirax
        /// </summary>
        [JsonProperty("Zirax")]
        public ZiraxSettings Zirax { get; set; } = new ZiraxSettings();

        /// <summary>
        /// Настройки AIM (Attack Instant Mob)
        /// </summary>
        [JsonProperty("AIM")]
        public AIMSettings AIM { get; set; } = new AIMSettings();

        /// <summary>
        /// Настройки размещения структур
        /// </summary>
        [JsonProperty("Placement")]
        public PlacementSettings Placement { get; set; } = new PlacementSettings();

        /// <summary>
        /// Настройки системы угроз
        /// </summary>
        [JsonProperty("Threat")]
        public ThreatSettings Threat { get; set; } = new ThreatSettings();

        /// <summary>
        /// Настройки экспансии
        /// </summary>
        [JsonProperty("Expansion")]
        public ExpansionSettings Expansion { get; set; } = new ExpansionSettings();
    }

    /// <summary>
    /// Настройки симуляции
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class SimulationSettings
    {
        /// <summary>
        /// Интервал тика симуляции (в миллисекундах)
        /// </summary>
        [JsonProperty("TickIntervalMs")]
        public int TickIntervalMs { get; set; } = 1000;

        /// <summary>
        /// Интервал сохранения состояния (в минутах)
        /// </summary>
        [JsonProperty("SaveIntervalMinutes")]
        public int SaveIntervalMinutes { get; set; } = 1;

        /// <summary>
        /// Интервал создания бэкапов (в часах)
        /// </summary>
        [JsonProperty("StateBackupIntervalHours")]
        public int StateBackupIntervalHours { get; set; } = 24;

        /// <summary>
        /// Количество хранимых бэкапов
        /// </summary>
        [JsonProperty("KeepBackupCount")]
        public int KeepBackupCount { get; set; } = 10;
    }

    /// <summary>
    /// Лимиты системы
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class LimitsSettings
    {
        /// <summary>
        /// Максимальное количество колоний на одной планете
        /// </summary>
        [JsonProperty("MaxColoniesPerPlayfield")]
        public int MaxColoniesPerPlayfield { get; set; } = 1;

        /// <summary>
        /// Максимальное количество активных AI кораблей
        /// </summary>
        [JsonProperty("MaxActiveAIVessels")]
        public int MaxActiveAIVessels { get; set; } = 5;

        /// <summary>
        /// Максимальное количество охранников рядом с колонией
        /// </summary>
        [JsonProperty("MaxGuardsNearColony")]
        public int MaxGuardsNearColony { get; set; } = 10;

        /// <summary>
        /// Максимальное количество строителей рядом с колонией
        /// </summary>
        [JsonProperty("MaxBuildersNearColony")]
        public int MaxBuildersNearColony { get; set; } = 5;

        /// <summary>
        /// Максимальное количество ресурсных аванпостов
        /// </summary>
        [JsonProperty("MaxResourceOutposts")]
        public int MaxResourceOutposts { get; set; } = 3;

        /// <summary>
        /// Максимальное количество волн дронов в час
        /// </summary>
        [JsonProperty("MaxDroneWavesPerHour")]
        public int MaxDroneWavesPerHour { get; set; } = 4;

        /// <summary>
        /// Максимальное количество AIM команд в минуту
        /// </summary>
        [JsonProperty("MaxAIMCommandsPerMinute")]
        public int MaxAIMCommandsPerMinute { get; set; } = 10;

        /// <summary>
        /// Максимальное количество запросов к ModAPI в секунду
        /// </summary>
        [JsonProperty("MaxRequestsPerSecond")]
        public int MaxRequestsPerSecond { get; set; } = 10;
    }

    /// <summary>
    /// Настройки фракции Zirax
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ZiraxSettings
    {
        /// <summary>
        /// ID фракции Zirax
        /// </summary>
        [JsonProperty("FactionId")]
        public int FactionId { get; set; } = 2;

        /// <summary>
        /// Конфигурации десантных кораблей
        /// </summary>
        [JsonProperty("DropShips")]
        public List<DropShipConfig> DropShips { get; set; } = new List<DropShipConfig>();

        /// <summary>
        /// Конфигурации стадий развития
        /// </summary>
        [JsonProperty("Stages")]
        public List<StageConfig> Stages { get; set; } = new List<StageConfig>();

        /// <summary>
        /// Конфигурации ресурсных аванпостов
        /// </summary>
        [JsonProperty("ResourceOutposts")]
        public List<ResourceOutpostConfig> ResourceOutposts { get; set; } = new List<ResourceOutpostConfig>();

        /// <summary>
        /// Конфигурации охранников
        /// </summary>
        [JsonProperty("Guards")]
        public List<GuardConfig> Guards { get; set; } = new List<GuardConfig>();
    }

    /// <summary>
    /// Конфигурация десантного корабля
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class DropShipConfig
    {
        /// <summary>
        /// Название префаба десантного корабля
        /// </summary>
        [JsonProperty("PrefabName")]
        public string PrefabName { get; set; } = string.Empty;

        /// <summary>
        /// Тип корабля (SV, CV, HV)
        /// </summary>
        [JsonProperty("Type")]
        public string Type { get; set; } = "SV";

        /// <summary>
        /// Высота появления десантного корабля в метрах
        /// </summary>
        [JsonProperty("SpawnAltitude")]
        public float SpawnAltitude { get; set; } = 500f;

        /// <summary>
        /// Длительность полета до цели в секундах
        /// </summary>
        [JsonProperty("FlightDurationSeconds")]
        public int FlightDurationSeconds { get; set; } = 30;
    }

    /// <summary>
    /// Конфигурация стадии развития колонии
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class StageConfig
    {
        /// <summary>
        /// Название стадии (ConstructionYard, BaseL1, BaseL2, BaseL3, BaseMax)
        /// </summary>
        [JsonProperty("Stage")]
        public string Stage { get; set; } = string.Empty;

        /// <summary>
        /// Название префаба структуры для этой стадии
        /// </summary>
        [JsonProperty("PrefabName")]
        public string PrefabName { get; set; } = string.Empty;

        /// <summary>
        /// Количество ресурсов, необходимое для достижения стадии
        /// </summary>
        [JsonProperty("RequiredResources")]
        public float RequiredResources { get; set; }

        /// <summary>
        /// Скорость производства ресурсов на этой стадии (ед/час)
        /// </summary>
        [JsonProperty("ProductionRate")]
        public float ProductionRate { get; set; }

        /// <summary>
        /// Минимальное время в секундах для достижения этой стадии
        /// </summary>
        [JsonProperty("MinTimeSeconds")]
        public int MinTimeSeconds { get; set; }
    }

    /// <summary>
    /// Конфигурация ресурсного аванпоста
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ResourceOutpostConfig
    {
        /// <summary>
        /// Тип добываемого ресурса (Iron, Copper, Promethium и т.д.)
        /// </summary>
        [JsonProperty("Type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Название префаба аванпоста
        /// </summary>
        [JsonProperty("PrefabName")]
        public string PrefabName { get; set; } = string.Empty;

        /// <summary>
        /// Скорость добычи ресурса (ед/час)
        /// </summary>
        [JsonProperty("ProductionRate")]
        public float ProductionRate { get; set; }
    }

    /// <summary>
    /// Конфигурация охранника
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class GuardConfig
    {
        /// <summary>
        /// Тип охранника (ZiraxMale, ZiraxFemale и т.д.)
        /// </summary>
        [JsonProperty("Type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Количество охранников этого типа
        /// </summary>
        [JsonProperty("Count")]
        public int Count { get; set; }
    }

    /// <summary>
    /// Настройки AIM (AI Manager - система управления AI)
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class AIMSettings
    {
        /// <summary>
        /// Список разрешенных AIM команд для управления AI
        /// </summary>
        [JsonProperty("AllowedCommands")]
        public List<string> AllowedCommands { get; set; } = new List<string> { "aim aga", "aim tdw", "aim adb" };

        /// <summary>
        /// Максимальное количество AIM команд в минуту (rate limiting)
        /// </summary>
        [JsonProperty("RateLimitPerMinute")]
        public int RateLimitPerMinute { get; set; } = 10;

        /// <summary>
        /// Радиус патрулирования охранников по умолчанию (в метрах)
        /// </summary>
        [JsonProperty("DefaultGuardRange")]
        public int DefaultGuardRange { get; set; } = 500;

        /// <summary>
        /// Время ожидания между волнами дронов (в минутах)
        /// </summary>
        [JsonProperty("DroneWaveCooldownMinutes")]
        public int DroneWaveCooldownMinutes { get; set; } = 15;
    }

    /// <summary>
    /// Настройки размещения структур в игровом мире
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class PlacementSettings
    {
        /// <summary>
        /// Минимальное расстояние от игроков при размещении структур (в метрах)
        /// </summary>
        [JsonProperty("MinDistanceFromPlayers")]
        public float MinDistanceFromPlayers { get; set; } = 500f;

        /// <summary>
        /// Минимальное расстояние от структур игроков при размещении (в метрах)
        /// </summary>
        [JsonProperty("MinDistanceFromPlayerStructures")]
        public float MinDistanceFromPlayerStructures { get; set; } = 1000f;

        /// <summary>
        /// Радиус поиска подходящего места для размещения структуры (в метрах)
        /// </summary>
        [JsonProperty("SearchRadius")]
        public float SearchRadius { get; set; } = 2000f;

        /// <summary>
        /// Предпочтительная высота размещения структур (в метрах над уровнем моря)
        /// </summary>
        [JsonProperty("PreferredAltitude")]
        public float PreferredAltitude { get; set; } = 150f;

        /// <summary>
        /// Максимальное количество попыток найти подходящее место для размещения
        /// </summary>
        [JsonProperty("MaxPlacementAttempts")]
        public int MaxPlacementAttempts { get; set; } = 10;
    }

    /// <summary>
    /// Настройки системы расчета уровня угрозы
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ThreatSettings
    {
        /// <summary>
        /// Вес влияния близости игроков на уровень угрозы
        /// </summary>
        [JsonProperty("PlayerProximityWeight")]
        public float PlayerProximityWeight { get; set; } = 10f;

        /// <summary>
        /// Вес влияния разрушения структур на уровень угрозы
        /// </summary>
        [JsonProperty("DestructionWeight")]
        public float DestructionWeight { get; set; } = 20f;

        /// <summary>
        /// Время затухания эффекта атаки на уровень угрозы (в минутах)
        /// </summary>
        [JsonProperty("AttackDecayMinutes")]
        public int AttackDecayMinutes { get; set; } = 60;

        /// <summary>
        /// Вес влияния стадии развития колонии на уровень угрозы
        /// </summary>
        [JsonProperty("StageValueWeight")]
        public float StageValueWeight { get; set; } = 5f;
    }

    /// <summary>
    /// Настройки экспансии колоний на другие планеты
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ExpansionSettings
    {
        /// <summary>
        /// Список целевых плейфилдов для экспансии
        /// </summary>
        [JsonProperty("TargetPlayfields")]
        public List<string> TargetPlayfields { get; set; } = new List<string> { "Omicron", "Ningues", "Tallodar" };

        /// <summary>
        /// Время путешествия до целевого плейфилда (в минутах)
        /// </summary>
        [JsonProperty("TravelTimeMinutes")]
        public int TravelTimeMinutes { get; set; } = 30;

        /// <summary>
        /// Минимальное время с момента достижения максимальной стадии до начала экспансии (в минутах)
        /// </summary>
        [JsonProperty("MinTimeSinceMaxStageMinutes")]
        public int MinTimeSinceMaxStageMinutes { get; set; } = 120;
    }
}
