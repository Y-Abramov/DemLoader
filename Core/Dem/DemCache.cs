using System;
using System.Globalization;
using System.IO;

namespace DemLoader.Core.Dem
{
    /// <summary>Дисковый кэш байтовых диапазонов, прочитанных по HTTP Range. Повторное чтение
    /// того же куска тайла бесплатно - транспорт бьётся в сеть только на первый запрос.
    /// Ключ - имя тайла плюс диапазон: разные куски одного тайла не должны затирать друг друга
    /// (аналог TileCache из Shared\Geo\Net, но там ключ zoom/x/y тайла карты, а не имя+диапазон).</summary>
    internal sealed class DemCache
    {
        private readonly string _root;

        public DemCache(string root) { _root = root; }

        public static string DefaultRoot
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(Path.Combine(Path.Combine(local, "Abr"), "DemLoader"), "cache");
            }
        }

        public bool TryGet(string tileName, long offset, int length, out byte[] data)
        {
            string path = PathFor(tileName, offset, length);
            if (File.Exists(path))
            {
                data = File.ReadAllBytes(path);
                // Имя файла кодирует точную длину - несовпадение значит, что файл на диске
                // повреждён или подменён. Не отдаём его как валидный кэш-хит.
                return data.Length == length;
            }

            data = null;
            return false;
        }

        public void Put(string tileName, long offset, int length, byte[] data)
        {
            string path = PathFor(tileName, offset, length);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
        }

        /// <summary>Сколько места занято на диске. Считается обходом папки, а не счётчиком в
        /// файле: кэш переживает перезапуск, его чистят руками из проводника, и любой счётчик
        /// рано или поздно разойдётся с содержимым. Тот же приём, что в TileCache (AbrBasemap).</summary>
        public long TotalBytes()
        {
            if (!Directory.Exists(_root)) return 0;

            long total = 0;
            foreach (string file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
                total += new FileInfo(file).Length;

            return total;
        }

        /// <summary>Папка удаляется целиком - Put создаёт её заново при первой же записи.</summary>
        public void Clear()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private string PathFor(string tileName, long offset, int length)
        {
            string dir = Sanitize(tileName);
            string file = offset.ToString(CultureInfo.InvariantCulture) + "_"
                        + length.ToString(CultureInfo.InvariantCulture) + ".bin";
            return Path.Combine(Path.Combine(_root, dir), file);
        }

        /// <summary>Имена тайлов Copernicus сами по себе безопасны для файловой системы
        /// (буквы/цифры/подчёркивания - см. DemTileKey.NameOfCell), но источник имени -
        /// параметр конструктора HttpTiffSource, а не только DemTileKey, поэтому подстраховка
        /// не лишняя.</summary>
        private static string Sanitize(string tileName)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = tileName.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            return new string(chars);
        }
    }
}
