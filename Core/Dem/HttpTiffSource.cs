using System;
using System.Net;
using System.Threading;
using DemLoader.Core.Tiff;
using DemLoader.Core.Net;

namespace DemLoader.Core.Dem
{
    /// <summary>Тайл Copernicus как источник байтов поверх сети, с диском в роли кэша.
    /// Читатель COG (TiffHeader/DemTileReader) видит только IByteRange и не знает про HTTP -
    /// тот же контракт, что и у локального файла (см. FileTiffSource).</summary>
    internal sealed class HttpTiffSource : IByteRange
    {
        private readonly string _url;
        private readonly string _tileName;
        private readonly ITileTransport _transport;
        private readonly DemCache _cache;
        private readonly string _userAgent;

        public HttpTiffSource(string url, string tileName, ITileTransport transport,
            DemCache cache, string userAgent)
        {
            _url = url;
            _tileName = tileName;
            _transport = transport;
            _cache = cache;
            _userAgent = userAgent;
        }

        public byte[] Read(long offset, int length)
        {
            byte[] cached;
            if (_cache.TryGet(_tileName, offset, length, out cached)) return cached;

            byte[] data;
            try
            {
                // IByteRange.Read синхронный и без токена - отмену через сеть протянуть
                // можно будет отдельной задачей, если понадобится; сейчас не нужна.
                data = _transport.GetRange(_url, _userAgent, offset, length, CancellationToken.None);
            }
            catch (WebException ex)
            {
                // 404 у Copernicus/S3-бакета значит "клетки сетки здесь нет" - это ожидаемый
                // исход для морской/пограничной клетки, а не отказ сети.
                if (IsHttpNotFound(ex)) throw new DemTileMissingException(_tileName);

                // Любой другой отказ (таймаут, обрыв, несовпадение длины у GetRange) летит как
                // есть - и, важно, до сюда не доходит запись в кэш ниже.
                throw;
            }

            // Кэш пишется только на этой строке, после успешного возврата из транспорта.
            // GetRange (Shared\Geo\Net\HttpTileTransport, Task 10) сам бросает на коротком/
            // длинном ответе и на не-206 - оборванная закачка никогда не долетает до Put.
            _cache.Put(_tileName, offset, length, data);
            return data;
        }

        /// <summary>GetRange (Shared\Geo\Net\HttpTileTransport, Task 10) освобождает ex.Response
        /// до re-throw - к моменту, когда исключение долетает сюда, HttpWebResponse.StatusCode
        /// читать поздно на net8.0 (Dispose() обнуляет внутренний HttpResponseMessage целиком,
        /// проверено эмпирически - это не просто охрана CheckDisposed(), код статуса физически
        /// не восстановить никаким reflection).
        ///
        /// Основной путь: GetRange сам снимает StatusCode до диспоза и кладёт его в
        /// ex.Data["HttpStatusCode"] - это переживает Dispose() на обеих таргетах и не зависит
        /// от текста исключения/локали. Запасной путь - defense-in-depth, не расчётный путь при
        /// исправном GetRange: если Data пуст (например, эту правку откатили или обошли), откат
        /// на числовой код в круглых скобках текста Message ("The remote server returned an
        /// error: (404) Not Found." и локализованные варианты вроде "Удаленный сервер возвратил
        /// ошибку: (404) Не найден." - окружающий текст локализован рантаймом, число в скобках
        /// нет).</summary>
        private static bool IsHttpNotFound(WebException ex)
        {
            if (ex.Data.Contains("HttpStatusCode"))
            {
                object code = ex.Data["HttpStatusCode"];
                if (code is int) return (int)code == (int)HttpStatusCode.NotFound;
            }

            return ex.Message.IndexOf("(404)", StringComparison.Ordinal) >= 0;
        }
    }

    /// <summary>Тайла нет у источника (HTTP 404) - ожидаемое "здесь нет данных" (морская клетка,
    /// клетка вне покрытия Copernicus), а не отказ сети. Вызывающий код (сборка сетки из тайлов)
    /// отличает это от прочих исключений и просто пропускает клетку.</summary>
    internal sealed class DemTileMissingException : Exception
    {
        public DemTileMissingException(string tileName)
            : base("Тайл " + tileName + " отсутствует у источника (404).") { }
    }
}
