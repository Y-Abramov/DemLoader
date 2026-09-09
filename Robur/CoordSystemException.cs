using System;

namespace DemLoader.Robur
{
    /// <summary>Отказ на уровне систем координат. Отдельный тип, чтобы диалог мог
    /// показать текст пользователю, а не «Object reference not set».</summary>
    internal sealed class CoordSystemException : Exception
    {
        public CoordSystemException(string message) : base(message) { }
    }
}
