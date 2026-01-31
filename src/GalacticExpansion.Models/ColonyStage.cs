using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GalacticExpansion.Models
{
    /// <summary>
    /// Стадии развития колонии Zirax.
    /// Колония эволюционирует от начальной посадки до максимального уровня,
    /// а затем переходит в цикл экспансии для захвата новых планет.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ColonyStage
    {
        /// <summary>
        /// Ожидание высадки десанта.
        /// На этой стадии колония существует только в state, физических структур нет.
        /// </summary>
        LandingPending = 0,

        /// <summary>
        /// Строительная площадка.
        /// Первая физическая структура, появляется после высадки десанта.
        /// Минимальное производство ресурсов.
        /// </summary>
        ConstructionYard = 1,

        /// <summary>
        /// База уровня 1.
        /// Базовая защита, начало добычи ресурсов.
        /// </summary>
        BaseL1 = 2,

        /// <summary>
        /// База уровня 2.
        /// Усиленная защита, активная добыча ресурсов.
        /// Может создавать ресурсные аванпосты.
        /// </summary>
        BaseL2 = 3,

        /// <summary>
        /// База уровня 3.
        /// Мощная защита, множественные аванпосты.
        /// Может отправлять патрульные корабли.
        /// </summary>
        BaseL3 = 4,

        /// <summary>
        /// Максимальный уровень базы.
        /// Полная защита, максимальное производство.
        /// Может создавать боевые корабли и начинать экспансию.
        /// </summary>
        BaseMax = 5,

        /// <summary>
        /// Цикл экспансии.
        /// Колония достигла максимума и готовится к захвату новых планет.
        /// Накапливает ресурсы для отправки экспедиций.
        /// </summary>
        ExpansionCycle = 6
    }

    /// <summary>
    /// Расширения для работы со стадиями колоний
    /// </summary>
    public static class ColonyStageExtensions
    {
        /// <summary>
        /// Проверяет, может ли колония создавать ресурсные аванпосты
        /// </summary>
        public static bool CanCreateResourceOutposts(this ColonyStage stage)
        {
            return stage >= ColonyStage.BaseL2;
        }

        /// <summary>
        /// Проверяет, может ли колония отправлять патрульные корабли
        /// </summary>
        public static bool CanDeployPatrolVessels(this ColonyStage stage)
        {
            return stage >= ColonyStage.BaseL3;
        }

        /// <summary>
        /// Проверяет, может ли колония начинать экспансию на другие планеты
        /// </summary>
        public static bool CanExpand(this ColonyStage stage)
        {
            return stage >= ColonyStage.BaseMax;
        }

        /// <summary>
        /// Возвращает следующую стадию развития
        /// </summary>
        public static ColonyStage GetNextStage(this ColonyStage stage)
        {
            return stage switch
            {
                ColonyStage.LandingPending => ColonyStage.ConstructionYard,
                ColonyStage.ConstructionYard => ColonyStage.BaseL1,
                ColonyStage.BaseL1 => ColonyStage.BaseL2,
                ColonyStage.BaseL2 => ColonyStage.BaseL3,
                ColonyStage.BaseL3 => ColonyStage.BaseMax,
                ColonyStage.BaseMax => ColonyStage.ExpansionCycle,
                ColonyStage.ExpansionCycle => ColonyStage.ExpansionCycle, // остается на этой стадии
                _ => throw new ArgumentException($"Unknown colony stage: {stage}")
            };
        }

        /// <summary>
        /// Возвращает предыдущую стадию (при откате из-за разрушения)
        /// </summary>
        public static ColonyStage? GetPreviousStage(this ColonyStage stage)
        {
            return stage switch
            {
                ColonyStage.LandingPending => null, // не может откатиться ниже
                ColonyStage.ConstructionYard => null, // не откатываем до LandingPending
                ColonyStage.BaseL1 => ColonyStage.ConstructionYard,
                ColonyStage.BaseL2 => ColonyStage.BaseL1,
                ColonyStage.BaseL3 => ColonyStage.BaseL2,
                ColonyStage.BaseMax => ColonyStage.BaseL3,
                ColonyStage.ExpansionCycle => ColonyStage.BaseMax,
                _ => throw new ArgumentException($"Unknown colony stage: {stage}")
            };
        }

        /// <summary>
        /// Возвращает числовое значение стадии для расчетов
        /// </summary>
        public static int GetStageValue(this ColonyStage stage)
        {
            return (int)stage;
        }
    }
}
