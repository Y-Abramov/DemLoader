using System.Collections.Generic;

namespace DemLoader.Core.Dem
{
    /// <summary>Несколько тайлов как одна поверхность. Тайлы не сшиваются в общий массив:
    /// область может задевать четыре ячейки по 29 МБ, и держать их в памяти целиком незачем.</summary>
    internal sealed class DemGrid
    {
        private readonly List<IDemTile> _tiles = new List<IDemTile>();

        public void Add(IDemTile tile) { _tiles.Add(tile); }

        public int TileCount { get { return _tiles.Count; } }

        public bool TryGetElevation(double lon, double lat, out double elevation)
        {
            for (int i = 0; i < _tiles.Count; i++)
                if (_tiles[i].Contains(lon, lat) && _tiles[i].TryGetElevation(lon, lat, out elevation))
                    return true;

            elevation = 0;
            return false;
        }
    }
}
