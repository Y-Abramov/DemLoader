using System;
using System.IO;
using System.Net;
using System.Threading;

namespace DemLoader.Core.Net
{
    /// <summary>HttpWebRequest, а не HttpClient: на net48 в процессе AutoCAD статический
    /// HttpClient живёт дольше домена и тянет за собой конфигурацию прокси хоста.</summary>
    internal sealed class HttpTileTransport : ITileTransport
    {
        private readonly int _timeoutMs;

        public HttpTileTransport(int timeoutMs) { _timeoutMs = timeoutMs; }

        public byte[] Get(string url, string userAgent, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

#pragma warning disable SYSLIB0014 // HttpWebRequest намеренно вместо HttpClient - см. doc-comment класса
            var request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
            request.Method = "GET";
            request.Timeout = _timeoutMs;
            request.ReadWriteTimeout = _timeoutMs;
            request.UserAgent = userAgent;
            request.Accept = "image/png,image/jpeg,image/*;q=0.8,*/*;q=0.5";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            // Abort() прерывает GetResponse(), если она зависла на медленном DNS/handshake -
            // без этого отмена сработала бы только после чтения ответа или по таймауту целиком.
            using (token.Register(request.Abort))
            {
                HttpWebResponse response;
                try
                {
                    response = (HttpWebResponse)request.GetResponse();
                }
                catch (WebException ex)
                {
                    if (ex.Response != null) ex.Response.Dispose();
                    // Abort() из token.Register выше рождает WebException (RequestCanceled),
                    // а не OperationCanceledException - переводим обратно, иначе отмена
                    // выглядит как обычный сетевой отказ и молча поглощается ретраем.
                    token.ThrowIfCancellationRequested();
                    throw;
                }

                using (response)
                using (var stream = response.GetResponseStream())
                using (var buffer = new MemoryStream())
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new WebException("Код ответа " + (int)response.StatusCode);

                    var chunk = new byte[16384];
                    int read;
                    while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        buffer.Write(chunk, 0, read);
                    }

                    return buffer.ToArray();
                }
            }
        }

        /// <summary>Часть объекта по диапазону байт. Нужна для COG: заголовок и отдельные блоки
        /// читаются кусками, файл целиком не качается.</summary>
        public byte[] GetRange(string url, string userAgent, long offset, int length, CancellationToken token)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException("offset");
            if (length <= 0) throw new ArgumentOutOfRangeException("length");
            token.ThrowIfCancellationRequested();

#pragma warning disable SYSLIB0014 // HttpWebRequest намеренно вместо HttpClient - см. doc-comment класса
            var request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
            request.Method = "GET";
            request.Timeout = _timeoutMs;
            request.ReadWriteTimeout = _timeoutMs;
            request.UserAgent = userAgent;
            request.Accept = "*/*";
            // Сжатие намеренно не включается: диапазон считается в байтах исходного объекта,
            // а прозрачная распаковка сбивает сверку длины ответа с запрошенной.
            request.AddRange(offset, offset + length - 1);

            // Abort() прерывает GetResponse(), если она зависла на медленном DNS/handshake -
            // без этого отмена сработала бы только после чтения ответа или по таймауту целиком.
            using (token.Register(request.Abort))
            {
                HttpWebResponse response;
                try
                {
                    response = (HttpWebResponse)request.GetResponse();
                }
                catch (WebException ex)
                {
                    // HttpWebResponse.StatusCode ниже по стеку читать уже поздно - Dispose()
                    // на net8.0 обнуляет внутренний HttpResponseMessage целиком, а не только
                    // выставляет флаг. Код статуса переживает диспоз только будучи снятым
                    // заранее и положенным в Data - туда более высокий уровень (HttpTiffSource)
                    // может заглянуть, не трогая сам объект ответа.
                    var http = ex.Response as HttpWebResponse;
                    if (http != null) ex.Data["HttpStatusCode"] = (int)http.StatusCode;

                    if (ex.Response != null) ex.Response.Dispose();
                    // Abort() из token.Register выше рождает WebException (RequestCanceled),
                    // а не OperationCanceledException - переводим обратно, иначе отмена
                    // выглядит как обычный сетевой отказ и молча поглощается ретраем.
                    token.ThrowIfCancellationRequested();
                    throw;
                }

                using (response)
                using (var stream = response.GetResponseStream())
                using (var buffer = new MemoryStream())
                {
                    // 200 вместо 206 = сервер проигнорировал Range и шлёт файл целиком. Молча
                    // вернуть первые length байт нельзя: для COG это чужой кусок под видом нужного.
                    if (response.StatusCode != HttpStatusCode.PartialContent)
                        throw new WebException("Сервер не поддерживает чтение по диапазону, код ответа "
                                               + (int)response.StatusCode);

                    var chunk = new byte[16384];
                    int read;
                    while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        buffer.Write(chunk, 0, read);
                        // Ответ 206 не может быть длиннее запрошенного диапазона. Если он всё же
                        // длиннее - это не тот кусок, обрывать чтение, а не копить чужие байты.
                        if (buffer.Length > length)
                            throw new WebException("Ответ длиннее запрошенного диапазона: "
                                                   + buffer.Length + " > " + length);
                    }

                    // Короткий ответ - оборванная закачка или прокси со своим представлением о
                    // диапазоне. Контракт метода точный: читатель COG считает длину массива равной
                    // запрошенной и иначе разберёт заголовок или блок по смещениям чужой длины.
                    if (buffer.Length < length)
                        throw new WebException("Ответ короче запрошенного диапазона: получено "
                                               + buffer.Length + " байт вместо " + length + ".");

                    return buffer.ToArray();
                }
            }
        }
    }
}
