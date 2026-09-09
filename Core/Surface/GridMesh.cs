using System.Collections.Generic;

namespace DemLoader.Core.Surface
{
    /// <summary>Треугольник регулярной сетки: индексы в списке точек поверхности.</summary>
    internal struct MeshTriangle
    {
        public int A;
        public int B;
        public int C;

        public MeshTriangle(int a, int b, int c) { A = a; B = b; C = c; }
    }

    /// <summary>Сетка узлов -> треугольники и приведение координат к локальному началу.
    ///
    /// Зачем локальное начало: Topomatic.Cad.Foundation.Triangulation.Node хранит X и Y
    /// как float. На координатах МСК порядка 1 483 000 у float остаётся около 0.1 м
    /// разрешения - соседние узлы 30-метровой сетки склеиваются, триангуляция вырождается.
    /// Сдвиг живёт только на время триангуляции: в поверхность точки пишутся в полных
    /// координатах.</summary>
    internal static class GridMesh
    {
        /// <summary>map[iy * nx + ix] - индекс точки в списке точек либо -1, если узел
        /// отсеян (nodata, вне контура, вне покрытия источника).</summary>
        public static List<MeshTriangle> BuildTriangles(int[] map, int nx, int ny)
        {
            var result = new List<MeshTriangle>();
            for (int iy = 0; iy + 1 < ny; iy++)
            {
                for (int ix = 0; ix + 1 < nx; ix++)
                {
                    int lb = map[iy * nx + ix];             // левый нижний
                    int rb = map[iy * nx + ix + 1];         // правый нижний
                    int lt = map[(iy + 1) * nx + ix];       // левый верхний
                    int rt = map[(iy + 1) * nx + ix + 1];   // правый верхний

                    // Ячейка режется по диагонали lb-rt. Треугольник строится только
                    // если все три его узла на месте: полуклетка лучше, чем дырка.
                    if (lb >= 0 && rb >= 0 && rt >= 0) result.Add(new MeshTriangle(lb, rb, rt));
                    if (lb >= 0 && rt >= 0 && lt >= 0) result.Add(new MeshTriangle(lb, rt, lt));
                }
            }
            return result;
        }

        public static double ToLocal(double value, double origin)
        {
            return value - origin;
        }
    }
}
