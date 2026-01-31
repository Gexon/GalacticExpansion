using System;
using System.IO;
using GalacticExpansion.Models;
using Newtonsoft.Json;
using NLog;

namespace GalacticExpansion
{
    /// <summary>
    /// Загружает и валидирует конфигурацию мода из Configuration.json.
    /// Предоставляет значения по умолчанию для всех параметров.
    /// </summary>
    public class ConfigurationLoader
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        private readonly string _configPath;

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="modDirectory">Путь к папке мода (обычно Content/Mods/GalacticExpansion)</param>
        public ConfigurationLoader(string modDirectory)
        {
            if (string.IsNullOrEmpty(modDirectory))
                throw new ArgumentException("Mod directory cannot be null or empty", nameof(modDirectory));

            _configPath = Path.Combine(modDirectory, "Configuration.json");
        }

        /// <summary>
        /// Загружает конфигурацию из файла.
        /// Если файл не существует или поврежден, создает конфигурацию по умолчанию.
        /// </summary>
        public Configuration Load()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    Logger.Warn($"Configuration file not found at {_configPath}. Creating default configuration...");
                    var defaultConfig = CreateDefaultConfiguration();
                    SaveConfiguration(defaultConfig);
                    return defaultConfig;
                }

                Logger.Info($"Loading configuration from {_configPath}...");
                
                var json = File.ReadAllText(_configPath);
                var config = JsonConvert.DeserializeObject<Configuration>(json);

                if (config == null)
                {
                    Logger.Error("Configuration file is empty or invalid. Using default configuration.");
                    return CreateDefaultConfiguration();
                }

                // Валидируем конфигурацию
                ValidateConfiguration(config);

                Logger.Info($"Configuration loaded successfully (version: {config.Version}, home playfield: {config.HomePlayfield})");
                LogConfigurationSummary(config);

                return config;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error loading configuration. Using default configuration.");
                return CreateDefaultConfiguration();
            }
        }

        /// <summary>
        /// Сохраняет конфигурацию в файл
        /// </summary>
        private void SaveConfiguration(Configuration config)
        {
            try
            {
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
                Logger.Info($"Configuration saved to {_configPath}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error saving configuration");
            }
        }

        /// <summary>
        /// Создает конфигурацию по умолчанию
        /// </summary>
        private Configuration CreateDefaultConfiguration()
        {
            Logger.Info("Creating default configuration");

            return new Configuration
            {
                Version = "1.0",
                LogLevel = "Information",
                HomePlayfield = "Akua",
                EnableExpansion = false,
                
                Simulation = new SimulationSettings
                {
                    TickIntervalMs = 1000,
                    SaveIntervalMinutes = 1,
                    StateBackupIntervalHours = 24,
                    KeepBackupCount = 10
                },
                
                Limits = new LimitsSettings
                {
                    MaxColoniesPerPlayfield = 1,
                    MaxActiveAIVessels = 5,
                    MaxGuardsNearColony = 10,
                    MaxBuildersNearColony = 5,
                    MaxResourceOutposts = 3,
                    MaxDroneWavesPerHour = 4,
                    MaxAIMCommandsPerMinute = 10,
                    MaxRequestsPerSecond = 10
                },
                
                Zirax = new ZiraxSettings
                {
                    FactionId = 2,
                    DropShips = new System.Collections.Generic.List<DropShipConfig>
                    {
                        new DropShipConfig
                        {
                            PrefabName = "GLEX_DropShip_T1",
                            Type = "SV",
                            SpawnAltitude = 500f,
                            FlightDurationSeconds = 30
                        }
                    },
                    Stages = new System.Collections.Generic.List<StageConfig>
                    {
                        new StageConfig { Stage = "ConstructionYard", PrefabName = "GLEX_ConstructionYard", RequiredResources = 0, ProductionRate = 100, MinTimeSeconds = 600 },
                        new StageConfig { Stage = "BaseL1", PrefabName = "GLEX_Base_L1", RequiredResources = 1000, ProductionRate = 150, MinTimeSeconds = 1800 },
                        new StageConfig { Stage = "BaseL2", PrefabName = "GLEX_Base_L2", RequiredResources = 3000, ProductionRate = 200, MinTimeSeconds = 3600 },
                        new StageConfig { Stage = "BaseL3", PrefabName = "GLEX_Base_L3", RequiredResources = 6000, ProductionRate = 250, MinTimeSeconds = 7200 },
                        new StageConfig { Stage = "BaseMax", PrefabName = "GLEX_Base_Max", RequiredResources = 10000, ProductionRate = 300, MinTimeSeconds = 14400 }
                    },
                    ResourceOutposts = new System.Collections.Generic.List<ResourceOutpostConfig>
                    {
                        new ResourceOutpostConfig { Type = "Iron", PrefabName = "GLEX_Miner_Iron", ProductionRate = 75 },
                        new ResourceOutpostConfig { Type = "Copper", PrefabName = "GLEX_Miner_Copper", ProductionRate = 50 }
                    },
                    Guards = new System.Collections.Generic.List<GuardConfig>
                    {
                        new GuardConfig { Type = "ZiraxMale", Count = 5 }
                    }
                },
                
                AIM = new AIMSettings
                {
                    AllowedCommands = new System.Collections.Generic.List<string> { "aim aga", "aim tdw", "aim adb" },
                    RateLimitPerMinute = 10,
                    DefaultGuardRange = 500,
                    DroneWaveCooldownMinutes = 15
                },
                
                Placement = new PlacementSettings
                {
                    MinDistanceFromPlayers = 500f,
                    MinDistanceFromPlayerStructures = 1000f,
                    SearchRadius = 2000f,
                    PreferredAltitude = 150f,
                    MaxPlacementAttempts = 10
                },
                
                Threat = new ThreatSettings
                {
                    PlayerProximityWeight = 10f,
                    DestructionWeight = 20f,
                    AttackDecayMinutes = 60,
                    StageValueWeight = 5f
                },
                
                Expansion = new ExpansionSettings
                {
                    TargetPlayfields = new System.Collections.Generic.List<string> { "Omicron", "Ningues", "Tallodar" },
                    TravelTimeMinutes = 30,
                    MinTimeSinceMaxStageMinutes = 120
                }
            };
        }

        /// <summary>
        /// Валидирует конфигурацию
        /// </summary>
        private void ValidateConfiguration(Configuration config)
        {
            // Проверяем критичные параметры
            if (config.Limits.MaxRequestsPerSecond <= 0)
            {
                Logger.Warn($"Invalid MaxRequestsPerSecond: {config.Limits.MaxRequestsPerSecond}. Using default: 10");
                config.Limits.MaxRequestsPerSecond = 10;
            }

            if (config.Simulation.TickIntervalMs < 100)
            {
                Logger.Warn($"TickIntervalMs too low: {config.Simulation.TickIntervalMs}. Setting to minimum: 100ms");
                config.Simulation.TickIntervalMs = 100;
            }

            if (string.IsNullOrWhiteSpace(config.HomePlayfield))
            {
                Logger.Warn("HomePlayfield is not set. Using default: Akua");
                config.HomePlayfield = "Akua";
            }
        }

        /// <summary>
        /// Логирует краткую сводку конфигурации
        /// </summary>
        private void LogConfigurationSummary(Configuration config)
        {
            Logger.Info("=== Configuration Summary ===");
            Logger.Info($"  Home Playfield: {config.HomePlayfield}");
            Logger.Info($"  Expansion Enabled: {config.EnableExpansion}");
            Logger.Info($"  Tick Interval: {config.Simulation.TickIntervalMs}ms");
            Logger.Info($"  Max Colonies/Playfield: {config.Limits.MaxColoniesPerPlayfield}");
            Logger.Info($"  Max Requests/Second: {config.Limits.MaxRequestsPerSecond}");
            Logger.Info($"  Zirax Faction ID: {config.Zirax.FactionId}");
            Logger.Info($"  Development Stages: {config.Zirax.Stages.Count}");
            Logger.Info("============================");
        }
    }
}
