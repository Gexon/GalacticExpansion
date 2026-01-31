using System;
using Newtonsoft.Json;

namespace GalacticExpansion.Models
{
    /// <summary>
    /// Представляет трехмерный вектор координат в мире Empyrion.
    /// Используется для позиций, вращений и других 3D данных.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class Vector3
    {
        /// <summary>
        /// Координата X (восток-запад)
        /// </summary>
        [JsonProperty("X")]
        public float X { get; set; }

        /// <summary>
        /// Координата Y (высота)
        /// </summary>
        [JsonProperty("Y")]
        public float Y { get; set; }

        /// <summary>
        /// Координата Z (север-юг)
        /// </summary>
        [JsonProperty("Z")]
        public float Z { get; set; }

        /// <summary>
        /// Конструктор по умолчанию (нулевой вектор)
        /// </summary>
        public Vector3()
        {
            X = 0;
            Y = 0;
            Z = 0;
        }

        /// <summary>
        /// Конструктор с координатами
        /// </summary>
        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Вычисляет расстояние до другой точки
        /// </summary>
        public float DistanceTo(Vector3 other)
        {
            float dx = X - other.X;
            float dy = Y - other.Y;
            float dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// Вычисляет расстояние до другой точки в 2D (игнорируя Y)
        /// Полезно для проверки расстояний на поверхности планеты
        /// </summary>
        public float DistanceTo2D(Vector3 other)
        {
            float dx = X - other.X;
            float dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public override string ToString()
        {
            return $"({X:F1}, {Y:F1}, {Z:F1})";
        }

        /// <summary>
        /// Создает копию вектора
        /// </summary>
        public Vector3 Clone()
        {
            return new Vector3(X, Y, Z);
        }
    }
}
