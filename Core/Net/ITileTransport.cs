using System.Threading;

namespace DemLoader.Core.Net
{
    /// <summary>Единственная точка выхода модуля в сеть. В тестах подменяется фейком,
    /// поэтому весь конвейер загрузки проверяется без интернета.</summary>
    internal interface ITileTransport
    {
        byte[] Get(string url, string userAgent, CancellationToken token);

        /// <summary>Часть объекта по диапазону байт. Нужна для COG: заголовок и отдельные блоки
        /// читаются кусками, файл целиком не качается. Длина результата всегда ровно
        /// <paramref name="length"/> - короткий или длинный ответ это отказ, а не частичные данные,
        /// поэтому запрашивать за концом файла нельзя.</summary>
        byte[] GetRange(string url, string userAgent, long offset, int length, CancellationToken token);
    }
}
