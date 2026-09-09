using System;

namespace DemLoader.Core.Geo
{
    /// <summary>Прямоугольник в географических координатах, градусы.</summary>
    internal struct GeoBox
    {
        public double MinLon;
        public double MinLat;
        public double MaxLon;
        public double MaxLat;

        /// <summary>Углы нормализуются в конструкторе (Min всегда меньше Max) - единая точка
        /// защиты. Перевёрнутый прямоугольник даёт отрицательные Columns/Rows у DownloadPlan,
        /// а отрицательный TileCount тихо проходит мимо потолка MaxTilesPerRun.</summary>
        public GeoBox(double minLon, double minLat, double maxLon, double maxLat)
        {
            MinLon = Math.Min(minLon, maxLon);
            MaxLon = Math.Max(minLon, maxLon);
            MinLat = Math.Min(minLat, maxLat);
            MaxLat = Math.Max(minLat, maxLat);
        }

        public double CenterLat { get { return (MinLat + MaxLat) / 2.0; } }
        public double CenterLon { get { return (MinLon + MaxLon) / 2.0; } }
    }
}
