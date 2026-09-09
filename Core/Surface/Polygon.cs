using System.Collections.Generic;

namespace DemLoader.Core.Surface
{
    /// <summary>Точка внутри контура (ray-casting, чётность пересечений). Перенос
    /// AreaSource.Inside из линейки Civil - тот же алгоритм, годится для любого замкнутого
    /// многоугольника, выпуклого или нет.</summary>
    internal static class Polygon
    {
        public static bool Contains(IList<double[]> ring, double x, double y)
        {
            bool inside = false;

            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                double xi = ring[i][0], yi = ring[i][1];
                double xj = ring[j][0], yj = ring[j][1];

                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                    inside = !inside;
            }

            return inside;
        }
    }
}
