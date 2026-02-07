using UnityEngine;

namespace GalacticExpansion.Core.Spawning
{
    /// <summary>
    /// Утилита для конвертации между типами GalacticExpansion.Models и UnityEngine.
    /// Используется для преобразования наших моделей в Unity типы при работе с IPlayfield API.
    /// </summary>
    public static class UnityTypeConverter
    {
        /// <summary>
        /// Конвертирует GalacticExpansion.Models.Vector3 в UnityEngine.Vector3.
        /// </summary>
        /// <param name="v">Вектор из наших моделей.</param>
        /// <returns>Unity вектор.</returns>
        public static Vector3 ToUnityVector3(GalacticExpansion.Models.Vector3 v)
        {
            return new Vector3(v.X, v.Y, v.Z);
        }

        /// <summary>
        /// Конвертирует UnityEngine.Vector3 в GalacticExpansion.Models.Vector3.
        /// </summary>
        /// <param name="v">Unity вектор.</param>
        /// <returns>Вектор наших моделей.</returns>
        public static GalacticExpansion.Models.Vector3 FromUnityVector3(Vector3 v)
        {
            return new GalacticExpansion.Models.Vector3(v.x, v.y, v.z);
        }

        /// <summary>
        /// Конвертирует углы Эйлера (Vector3) в Unity Quaternion.
        /// </summary>
        /// <param name="eulerAngles">Углы Эйлера (X, Y, Z в градусах).</param>
        /// <returns>Unity Quaternion.</returns>
        public static Quaternion ToUnityQuaternion(GalacticExpansion.Models.Vector3 eulerAngles)
        {
            return Quaternion.Euler(eulerAngles.X, eulerAngles.Y, eulerAngles.Z);
        }

        /// <summary>
        /// Конвертирует Unity Quaternion в углы Эйлера (Vector3).
        /// </summary>
        /// <param name="rotation">Unity Quaternion.</param>
        /// <returns>Углы Эйлера (X, Y, Z в градусах).</returns>
        public static GalacticExpansion.Models.Vector3 FromUnityQuaternion(Quaternion rotation)
        {
            var euler = rotation.eulerAngles;
            return new GalacticExpansion.Models.Vector3(euler.x, euler.y, euler.z);
        }
    }
}
