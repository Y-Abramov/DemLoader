using System;
using System.IO;

namespace DemLoader.Core.Tiff
{
    /// <summary>Привязка пикселей к градусам по тегам самого файла.
    ///
    /// Захардкоживать здесь ничего нельзя: у Copernicus размер тайла и шаг по долготе меняются
    /// широтными поясами (1" до 50 градусов, 1,5" до 60, 2" до 70, 3" до 80), а по широте шаг
    /// всегда 1". Формула «3600 пикселей на градус» даёт сдвиг растра на градус в северных
    /// проектах - ровно там, где рельеф и нужен.</summary>
    internal sealed class GeoTiffModel
    {
        private const ushort GeoKeyRasterType = 1025;
        private const int RasterPixelIsArea = 1;

        public readonly double OriginLon;
        public readonly double OriginLat;
        public readonly double ScaleLon;
        public readonly double ScaleLat;
        public readonly int Width;
        public readonly int Height;
        public readonly bool PixelIsArea;

        private GeoTiffModel(double originLon, double originLat, double scaleLon, double scaleLat,
            int width, int height, bool pixelIsArea)
        {
            OriginLon = originLon;
            OriginLat = originLat;
            ScaleLon = scaleLon;
            ScaleLat = scaleLat;
            Width = width;
            Height = height;
            PixelIsArea = pixelIsArea;
        }

        /// <summary>Только для тестов: собирает модель из готовых значений, без чтения TIFF.
        /// Продакшн-путь <see cref="From"/> не затронут - у него своё чтение и проверка тегов.</summary>
        internal static GeoTiffModel FromRaw(double originLon, double originLat, double scaleLon, double scaleLat,
            int width, int height, bool pixelIsArea)
        {
            return new GeoTiffModel(originLon, originLat, scaleLon, scaleLat, width, height, pixelIsArea);
        }

        public static GeoTiffModel From(TiffHeader header)
        {
            double[] scale = header.RequireDoubleArray(TiffHeader.TagModelPixelScale);
            double[] tie = header.RequireDoubleArray(TiffHeader.TagModelTiepoint);

            if (scale.Length < 2)
                throw new InvalidDataException("Тег ModelPixelScale хранит меньше двух значений.");
            if (tie.Length < 6)
                throw new InvalidDataException("Тег ModelTiepoint хранит меньше шести значений.");
            if (tie[0] != 0.0 || tie[1] != 0.0)
                throw new InvalidDataException("Привязка задана не от пикселя (0,0), такой файл не поддерживается.");

            return new GeoTiffModel(tie[3], tie[4], scale[0], scale[1],
                (int)header.ImageWidth, (int)header.ImageLength, ReadPixelIsArea(header));
        }

        private static bool ReadPixelIsArea(TiffHeader header)
        {
            if (!header.Has(TiffHeader.TagGeoKeyDirectory)) return true;

            uint[] keys = header.RequireUInt32Array(TiffHeader.TagGeoKeyDirectory);
            // Формат: заголовок из четырёх значений, дальше по четыре на ключ: id, место, число, значение.
            for (int i = 4; i + 3 < keys.Length; i += 4)
                if (keys[i] == GeoKeyRasterType) return keys[i + 3] == RasterPixelIsArea;

            return true;
        }

        /// <summary>Центр пикселя в градусах. Для схемы «пиксель - площадка» центр смещён
        /// на полшага от угла привязки, для схемы «пиксель - точка» совпадает с узлом.</summary>
        public void ToDegrees(int column, int row, out double lon, out double lat)
        {
            double shift = PixelIsArea ? 0.5 : 0.0;
            lon = OriginLon + (column + shift) * ScaleLon;
            lat = OriginLat - (row + shift) * ScaleLat;
        }

        public void ToPixel(double lon, double lat, out double column, out double row)
        {
            double shift = PixelIsArea ? 0.5 : 0.0;
            column = (lon - OriginLon) / ScaleLon - shift;
            row = (OriginLat - lat) / ScaleLat - shift;
        }

        /// <summary>Проверка попадания точки в тайл - в тех же пиксельных координатах,
        /// что и <see cref="ToPixel"/>: для схемы «площадка» это значит с полупиксельным
        /// запасом по каждому краю растра, а не по сырому индексу пикселя.</summary>
        public bool Contains(double lon, double lat)
        {
            double column, row;
            ToPixel(lon, lat, out column, out row);

            double shift = PixelIsArea ? 0.5 : 0.0;
            const double epsilon = 1e-9;
            return column >= -shift - epsilon && row >= -shift - epsilon
                && column <= Width - 1 + shift + epsilon && row <= Height - 1 + shift + epsilon;
        }
    }
}
