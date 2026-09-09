using System;

namespace DemLoader.Robur
{
    /// <summary>Отказ при записи поверхности. Отдельный тип: сообщение показывается
    /// пользователю дословно.</summary>
    internal sealed class TerrainWriteException : Exception
    {
        public TerrainWriteException(string message) : base(message) { }
    }
}
