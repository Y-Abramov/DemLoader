using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DemLoader.Core.Tiff
{
    /// <summary>Каталог тегов IFD0 классического TIFF. Значения длиннее четырёх байт лежат по
    /// смещению - дочитываются тем же IByteRange, поэтому заголовок работает и на файле, и на
    /// удалённом объекте без полной загрузки.</summary>
    internal sealed class TiffHeader
    {
        private struct Entry
        {
            public ushort Type;
            public uint Count;
            public uint Offset;     // смещение значения либо сами байты, если влезли
            public byte[] Inline;
        }

        private readonly IByteRange _source;
        private readonly bool _littleEndian;
        private readonly Dictionary<ushort, Entry> _entries = new Dictionary<ushort, Entry>();

        // Размер значения по коду типа TIFF: 1 BYTE, 2 ASCII, 3 SHORT, 4 LONG, 5 RATIONAL,
        // 6 SBYTE, 7 UNDEFINED, 8 SSHORT, 9 SLONG, 10 SRATIONAL, 11 FLOAT, 12 DOUBLE.
        private static readonly int[] TypeSize = { 0, 1, 1, 2, 4, 8, 1, 1, 2, 4, 8, 4, 8 };

        public const ushort TagImageWidth = 256;
        public const ushort TagImageLength = 257;
        public const ushort TagBitsPerSample = 258;
        public const ushort TagCompression = 259;
        public const ushort TagSamplesPerPixel = 277;
        public const ushort TagPredictor = 317;
        public const ushort TagTileWidth = 322;
        public const ushort TagTileLength = 323;
        public const ushort TagTileOffsets = 324;
        public const ushort TagTileByteCounts = 325;
        public const ushort TagSampleFormat = 339;
        public const ushort TagModelPixelScale = 33550;
        public const ushort TagModelTiepoint = 33922;
        public const ushort TagGeoKeyDirectory = 34735;
        public const ushort TagGdalNoData = 42113;

        private TiffHeader(IByteRange source, bool littleEndian)
        {
            _source = source;
            _littleEndian = littleEndian;
        }

        public static TiffHeader Read(IByteRange source)
        {
            byte[] start = source.Read(0, 8);
            if (start.Length < 8) throw new InvalidDataException("Файл короче заголовка TIFF.");

            bool little;
            if (start[0] == 'I' && start[1] == 'I') little = true;
            else if (start[0] == 'M' && start[1] == 'M') little = false;
            else throw new InvalidDataException("Файл не в формате TIFF: нет сигнатуры порядка байт.");

            var header = new TiffHeader(source, little);

            int magic = header.ToUInt16(start, 2);
            if (magic == 43) throw new InvalidDataException("BigTIFF не поддерживается, ожидается классический TIFF.");
            if (magic != 42) throw new InvalidDataException("Файл не в формате TIFF: магическое число " + magic + ".");

            uint ifd = header.ToUInt32(start, 4);
            header.ReadDirectory(ifd);
            return header;
        }

        private void ReadDirectory(uint offset)
        {
            byte[] countBytes = _source.Read(offset, 2);
            if (countBytes.Length < 2) throw new InvalidDataException("Каталог тегов TIFF недоступен.");

            int count = ToUInt16(countBytes, 0);
            byte[] table = _source.Read(offset + 2, count * 12);
            if (table.Length < count * 12) throw new InvalidDataException("Каталог тегов TIFF обрезан.");

            for (int i = 0; i < count; i++)
            {
                int at = i * 12;
                var entry = new Entry
                {
                    Type = ToUInt16(table, at + 2),
                    Count = ToUInt32(table, at + 4)
                };

                long size = (long)SizeOf(entry.Type) * entry.Count;
                if (size <= 4)
                {
                    entry.Inline = new byte[4];
                    Array.Copy(table, at + 8, entry.Inline, 0, 4);
                }
                else entry.Offset = ToUInt32(table, at + 8);

                _entries[ToUInt16(table, at)] = entry;
            }
        }

        public bool Has(ushort tag) { return _entries.ContainsKey(tag); }

        public uint RequireUInt32(ushort tag)
        {
            uint[] values = RequireUInt32Array(tag);
            return values[0];
        }

        public int RequireInt32(ushort tag) { return (int)RequireUInt32(tag); }

        /// <summary>Тег с умолчанием - только там, где умолчание задано стандартом TIFF
        /// (Predictor 1, SampleFormat 1). Для геометрии и таблицы тайлов умолчаний нет:
        /// отсутствие такого тега означает файл не того профиля, и это ошибка, а не ноль.</summary>
        public int OptionalInt32(ushort tag, int fallback)
        {
            return Has(tag) ? RequireInt32(tag) : fallback;
        }

        public uint[] RequireUInt32Array(ushort tag)
        {
            Entry entry;
            if (!_entries.TryGetValue(tag, out entry))
                throw new InvalidDataException("В файле нет обязательного тега TIFF " +
                    tag.ToString(CultureInfo.InvariantCulture) + ".");

            if (entry.Type != 1 && entry.Type != 3 && entry.Type != 4)
                throw new InvalidDataException("Тег " + tag + " имеет тип TIFF " + entry.Type +
                    ", ожидался BYTE/SHORT/LONG (беззнаковое целое).");

            byte[] data = ValueBytes(entry, tag);
            int size = SizeOf(entry.Type);
            var result = new uint[entry.Count];

            for (int i = 0; i < entry.Count; i++)
            {
                if (size == 2) result[i] = ToUInt16(data, i * 2);
                else if (size == 4) result[i] = ToUInt32(data, i * 4);
                else if (size == 1) result[i] = data[i];
            }

            return result;
        }

        public double[] RequireDoubleArray(ushort tag)
        {
            Entry entry;
            if (!_entries.TryGetValue(tag, out entry))
                throw new InvalidDataException("В файле нет обязательного тега TIFF " +
                    tag.ToString(CultureInfo.InvariantCulture) + ".");

            if (entry.Type != 12)
                throw new InvalidDataException("Тег " + tag + " имеет тип TIFF " + entry.Type +
                    ", ожидался DOUBLE.");

            byte[] data = ValueBytes(entry, tag);
            var result = new double[entry.Count];
            for (int i = 0; i < entry.Count; i++) result[i] = ToDouble(data, i * 8);
            return result;
        }

        public string OptionalAscii(ushort tag)
        {
            Entry entry;
            if (!_entries.TryGetValue(tag, out entry)) return null;
            return System.Text.Encoding.ASCII.GetString(ValueBytes(entry, tag)).TrimEnd('\0');
        }

        private byte[] ValueBytes(Entry entry, ushort tag)
        {
            long size = (long)SizeOf(entry.Type) * entry.Count;
            if (size <= 4) return entry.Inline;

            if (size > int.MaxValue)
                throw new InvalidDataException("Тег " + tag + " требует слишком много памяти (" + size + " байт).");

            byte[] data = _source.Read(entry.Offset, (int)size);
            if (data.Length != size)
                throw new InvalidDataException("Тег " + tag + " обрезан: получено " + data.Length +
                    " байт вместо " + size + ".");

            return data;
        }

        private static int SizeOf(ushort type)
        {
            if (type == 0 || type >= TypeSize.Length)
                throw new InvalidDataException("Неизвестный тип значения TIFF " + type + ".");
            return TypeSize[type];
        }

        public uint ImageWidth { get { return RequireUInt32(TagImageWidth); } }
        public uint ImageLength { get { return RequireUInt32(TagImageLength); } }
        public uint TileWidth { get { return RequireUInt32(TagTileWidth); } }
        public uint TileLength { get { return RequireUInt32(TagTileLength); } }
        public int Compression { get { return RequireInt32(TagCompression); } }
        public int Predictor { get { return OptionalInt32(TagPredictor, 1); } }
        public int SampleFormat { get { return OptionalInt32(TagSampleFormat, 1); } }
        public int BitsPerSample { get { return RequireInt32(TagBitsPerSample); } }
        public int SamplesPerPixel { get { return OptionalInt32(TagSamplesPerPixel, 1); } }
        public uint[] TileOffsets { get { return RequireUInt32Array(TagTileOffsets); } }
        public uint[] TileByteCounts { get { return RequireUInt32Array(TagTileByteCounts); } }

        public int TilesAcross { get { return (int)((ImageWidth + TileWidth - 1) / TileWidth); } }
        public int TilesDown { get { return (int)((ImageLength + TileLength - 1) / TileLength); } }

        private ushort ToUInt16(byte[] data, int at)
        {
            return _littleEndian
                ? (ushort)(data[at] | (data[at + 1] << 8))
                : (ushort)((data[at] << 8) | data[at + 1]);
        }

        private uint ToUInt32(byte[] data, int at)
        {
            return _littleEndian
                ? (uint)(data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24))
                : (uint)((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]);
        }

        private double ToDouble(byte[] data, int at)
        {
            if (BitConverter.IsLittleEndian == _littleEndian) return BitConverter.ToDouble(data, at);

            var swapped = new byte[8];
            for (int i = 0; i < 8; i++) swapped[i] = data[at + 7 - i];
            return BitConverter.ToDouble(swapped, 0);
        }
    }
}
