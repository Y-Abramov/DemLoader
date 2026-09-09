using System;
using System.Globalization;
using System.IO;

namespace DemLoader.Core.Net
{
    /// <summary>Дисковый кэш тайлов. Повторная загрузка той же области бесплатна,
    /// и прерванная загрузка не начинается с нуля.</summary>
    internal sealed class TileCache
    {
        private readonly string _root;

        public TileCache(string root) { _root = root; }

        public static string DefaultRoot
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(Path.Combine(Path.Combine(local, "Abr"), "Basemap"), "cache");
            }
        }

        public bool TryGet(string sourceId, int zoom, int x, int y, out byte[] data)
        {
            string path = PathFor(sourceId, zoom, x, y);
            if (File.Exists(path))
            {
                data = File.ReadAllBytes(path);
                return data.Length > 0;
            }

            data = null;
            return false;
        }

        public void Put(string sourceId, int zoom, int x, int y, byte[] data)
        {
            string path = PathFor(sourceId, zoom, x, y);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
        }

        public long TotalBytes()
        {
            if (!Directory.Exists(_root)) return 0;

            long total = 0;
            foreach (string file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
                total += new FileInfo(file).Length;

            return total;
        }

        public void Clear()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private string PathFor(string sourceId, int zoom, int x, int y)
        {
            string z = zoom.ToString(CultureInfo.InvariantCulture);
            string xs = x.ToString(CultureInfo.InvariantCulture);
            string ys = y.ToString(CultureInfo.InvariantCulture) + ".png";
            return Path.Combine(Path.Combine(Path.Combine(Path.Combine(_root, sourceId), z), xs), ys);
        }
    }
}
