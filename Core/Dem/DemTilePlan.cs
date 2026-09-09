using System;
using System.Collections.Generic;
using DemLoader.Core.Geo;

namespace DemLoader.Core.Dem
{
    internal sealed class DemTileRef
    {
        public int Lat;
        public int Lon;
        public string Name;
    }

    /// <summary>Список ячеек 1x1 градус, накрывающих область.</summary>
    internal sealed class DemTilePlan
    {
        public readonly List<DemTileRef> Tiles = new List<DemTileRef>();

        public static DemTilePlan Create(GeoBox box, int resolutionMarker)
        {
            var plan = new DemTilePlan();

            int latFrom = (int)Math.Floor(box.MinLat);
            int lonFrom = (int)Math.Floor(box.MinLon);

            // Верхняя граница ровно на целом градусе принадлежит нижней ячейке: область
            // 55,5..56,0 лежит в тайле N55 целиком, тайл N56 качать незачем.
            int latTo = (int)Math.Ceiling(box.MaxLat) - 1;
            int lonTo = (int)Math.Ceiling(box.MaxLon) - 1;
            if (latTo < latFrom) latTo = latFrom;
            if (lonTo < lonFrom) lonTo = lonFrom;

            for (int lat = latFrom; lat <= latTo; lat++)
                for (int lon = lonFrom; lon <= lonTo; lon++)
                    plan.Tiles.Add(new DemTileRef
                    {
                        Lat = lat,
                        Lon = lon,
                        Name = DemTileKey.NameOfCell(lat, lon, resolutionMarker)
                    });

            return plan;
        }
    }
}
