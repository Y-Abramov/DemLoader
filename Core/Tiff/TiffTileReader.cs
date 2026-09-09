using System;
using System.IO;
using System.IO.Compression;

namespace DemLoader.Core.Tiff
{
    /// <summary>Распаковка одного внутреннего блока GeoTIFF в отметки.
    ///
    /// Copernicus DEM отдаёт Adobe Deflate (сжатие 8) с предиктором 3 - «плавающим». Предиктор 3
    /// не просто разность соседей: байты числа разложены по плоскостям (сначала все первые байты
    /// строки, потом все вторые и так далее), и после разностного восстановления их надо собрать
    /// обратно. Наивная реализация не падает, а возвращает правдоподобный мусор - потому здесь
    /// точный порядок из libtiff, а тест сверяется с независимым источником отметок.</summary>
    internal static class TiffTileReader
    {
        public static float[] Decode(byte[] packed, int width, int height, int compression,
            int predictor, int bytesPerSample, int samplesPerPixel, bool littleEndian)
        {
            byte[] raw = Inflate(packed, compression, width * height * bytesPerSample * samplesPerPixel);

            if (predictor == 2) UndoHorizontal(raw, width, height, bytesPerSample, samplesPerPixel);
            else if (predictor == 3) UndoFloating(raw, width, height, bytesPerSample, samplesPerPixel);
            else if (predictor != 1)
                throw new NotSupportedException("Предиктор TIFF " + predictor + " не поддерживается.");

            return ToFloats(raw, width * height * samplesPerPixel, bytesPerSample, littleEndian);
        }

        private static byte[] Inflate(byte[] packed, int compression, int expectedBytes)
        {
            if (compression == 1)
            {
                if (packed.Length < expectedBytes)
                    throw new InvalidDataException("Блок без сжатия короче ожидаемого: получено " +
                        packed.Length + " байт вместо " + expectedBytes + ".");

                return packed;
            }

            if (compression != 8 && compression != 32946)
                throw new NotSupportedException("Сжатие TIFF " + compression +
                    " не поддерживается. Поддерживаются: без сжатия (1) и Deflate (8, 32946).");

            // Adobe Deflate - поток zlib: два байта заголовка и контрольная сумма в конце.
            // DeflateStream принимает только сырой deflate, поэтому заголовок снимаем сами.
            // ZLibStream появился в .NET 6, на net48 его нет - отсюда ручной пропуск.
            int skip = LooksLikeZlib(packed) ? 2 : 0;

            using (var input = new MemoryStream(packed, skip, packed.Length - skip))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream(expectedBytes))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                    output.Write(buffer, 0, read);

                byte[] raw = output.ToArray();
                if (raw.Length < expectedBytes)
                    throw new InvalidDataException("Блок распакован не полностью: получено " +
                        raw.Length + " байт вместо " + expectedBytes + ".");

                return raw;
            }
        }

        private static bool LooksLikeZlib(byte[] data)
        {
            if (data.Length < 2) return false;
            if ((data[0] & 0x0F) != 8) return false;               // метод сжатия deflate
            return ((data[0] << 8) + data[1]) % 31 == 0;           // контрольное поле заголовка zlib
        }

        // Предиктор 2 здесь - НЕ учебниковая числовая разность соседних отсчётов с переносом
        // через границу байтов, а независимая байтовая разность на фиксированном шаге
        // (bytesPerSample * samplesPerPixel). Для 1-байтных сэмплов это совпадает с настоящей
        // разностью значения. Для более широких сэмплов - нет: перенос из младшего байта в
        // старший теряется молча (см. охранное исключение ниже). Copernicus DEM использует
        // предиктор 3 (UndoFloating), этот путь реальным источником пока не задействуется.
        private static void UndoHorizontal(byte[] raw, int width, int height, int bytesPerSample, int samplesPerPixel)
        {
            // Байтовая (не числовая) разность корректна только для 1-байтных сэмплов - для них
            // "разница байта" и "разница сэмпла" совпадают. Для более широких сэмплов (обычный
            // случай реальных DEM с предиктором 2) нужен перенос через границу байтов внутри
            // сэмпла, которого этот код не делает - молча даёт неверное значение при переносе.
            // Пока это не проверено и не нужно ни одному текущему источнику - явный отказ вместо
            // тихо неверных отметок.
            if (bytesPerSample != 1)
                throw new NotSupportedException("Предиктор 2 (горизонтальный) реализован только для " +
                    "1-байтных сэмплов, здесь " + bytesPerSample + " байт(а). Нужна арифметика с " +
                    "переносом через границу байтов сэмпла.");

            int stride = width * bytesPerSample * samplesPerPixel;

            for (int row = 0; row < height; row++)
            {
                int start = row * stride;
                for (int i = bytesPerSample * samplesPerPixel; i < stride; i++)
                    raw[start + i] += raw[start + i - bytesPerSample * samplesPerPixel];
            }
        }

        private static void UndoFloating(byte[] raw, int width, int height, int bytesPerSample, int samplesPerPixel)
        {
            int samples = width * samplesPerPixel;
            int stride = samples * bytesPerSample;
            var line = new byte[stride];

            for (int row = 0; row < height; row++)
            {
                int start = row * stride;

                // Шаг 1: разностное восстановление байтов, шаг равен числу компонент пикселя.
                for (int i = samplesPerPixel; i < stride; i++)
                    raw[start + i] += raw[start + i - samplesPerPixel];

                // Шаг 2: сборка числа из байтовых плоскостей. Порядок байт в собранном числе -
                // от старшего к младшему, поэтому на little-endian платформе индекс переворачивается.
                for (int sample = 0; sample < samples; sample++)
                    for (int b = 0; b < bytesPerSample; b++)
                        line[sample * bytesPerSample + (bytesPerSample - b - 1)] = raw[start + b * samples + sample];

                Buffer.BlockCopy(line, 0, raw, start, stride);
            }
        }

        private static float[] ToFloats(byte[] raw, int count, int bytesPerSample, bool littleEndian)
        {
            if (bytesPerSample != 4)
                throw new NotSupportedException("Ожидаются 32-битные значения, в файле " +
                    (bytesPerSample * 8) + "-битные.");

            var values = new float[count];

            if (littleEndian == BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(raw, 0, values, 0, count * 4);
                return values;
            }

            var swap = new byte[4];
            for (int i = 0; i < count; i++)
            {
                swap[0] = raw[i * 4 + 3]; swap[1] = raw[i * 4 + 2];
                swap[2] = raw[i * 4 + 1]; swap[3] = raw[i * 4];
                values[i] = BitConverter.ToSingle(swap, 0);
            }

            return values;
        }
    }
}
