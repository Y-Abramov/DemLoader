using System.IO;
using DemLoader.Core.Tiff;

namespace DemLoader.Core.Dem
{
    /// <summary>Локальный GeoTIFF как источник байтов - оффлайн-путь и случай, когда файл уже
    /// лежит на диске у пользователя. В отличие от тестового Tests\FileByteRange короткое чтение
    /// не обрезает результат молча: здесь это признак битого/чужого файла, а не конца фикстуры,
    /// и читатель COG выше по стеку рассчитывает на массив ровно запрошенной длины.</summary>
    internal sealed class FileTiffSource : IByteRange
    {
        private readonly string _path;

        public FileTiffSource(string path) { _path = path; }

        public byte[] Read(long offset, int length)
        {
            using (var stream = File.OpenRead(_path))
            {
                if (offset + length > stream.Length)
                    throw new InvalidDataException(
                        "Файл " + _path + " короче запрошенного диапазона: смещение " + offset +
                        ", длина " + length + ", размер файла " + stream.Length + ".");

                stream.Seek(offset, SeekOrigin.Begin);
                var buffer = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int chunk = stream.Read(buffer, read, length - read);
                    if (chunk == 0)
                        throw new InvalidDataException(
                            "Файл " + _path + " оборвался раньше ожидаемого при чтении диапазона: " +
                            "смещение " + offset + ", длина " + length + ", прочитано " + read + ".");
                    read += chunk;
                }

                return buffer;
            }
        }
    }
}
