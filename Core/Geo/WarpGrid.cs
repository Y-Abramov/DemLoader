using System;

namespace DemLoader.Core.Geo
{
    /// <summary>Разреженная сетка «МСК - широта/долгота» с билинейной интерполяцией внутри ячейки.
    /// Родной API преобразования координат вызывается только в узлах: на растре 8000x8000
    /// вызов на каждый пиксель дал бы 64 миллиона переходов через управляемую обёртку.
    /// Сетка приходит готовыми данными, поэтому Core не знает про Autodesk.</summary>
    internal sealed class WarpGrid
    {
        public readonly int Columns;
        public readonly int Rows;
        public readonly double MinX;
        public readonly double MinY;
        public readonly double MaxX;
        public readonly double MaxY;

        private readonly double[] _lon;
        private readonly double[] _lat;

        public WarpGrid(int columns, int rows, double minX, double minY, double maxX, double maxY)
        {
            if (columns < 2 || rows < 2) throw new ArgumentException("Сетка должна иметь минимум 2x2 узла.");

            Columns = columns;
            Rows = rows;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            _lon = new double[columns * rows];
            _lat = new double[columns * rows];
        }

        public void SetNode(int column, int row, double lon, double lat)
        {
            _lon[row * Columns + column] = lon;
            _lat[row * Columns + column] = lat;
        }

        /// <summary>Точка за пределами области сетки насыщается до края (значение крайнего узла),
        /// а не экстраполируется линейно дальше. Caller должен строить сетку по границам своей
        /// области, но чужая рассинхронизация Warper/WarpGrid тогда даёт плоский край, а не
        /// неограниченно растущую ошибку - вылет наружу заметен сразу, а не тихо.</summary>
        public void Map(double x, double y, out double lon, out double lat)
        {
            double fx = (x - MinX) / (MaxX - MinX) * (Columns - 1);
            double fy = (y - MinY) / (MaxY - MinY) * (Rows - 1);

            int cx = Clamp((int)Math.Floor(fx), 0, Columns - 2);
            int cy = Clamp((int)Math.Floor(fy), 0, Rows - 2);
            double tx = Clamp01(fx - cx);
            double ty = Clamp01(fy - cy);

            lon = Bilinear(_lon, cx, cy, tx, ty);
            lat = Bilinear(_lat, cx, cy, tx, ty);
        }

        private double Bilinear(double[] values, int cx, int cy, double tx, double ty)
        {
            double v00 = values[cy * Columns + cx];
            double v10 = values[cy * Columns + cx + 1];
            double v01 = values[(cy + 1) * Columns + cx];
            double v11 = values[(cy + 1) * Columns + cx + 1];

            return v00 * (1 - tx) * (1 - ty) + v10 * tx * (1 - ty) + v01 * (1 - tx) * ty + v11 * tx * ty;
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private static double Clamp01(double value)
        {
            return value < 0.0 ? 0.0 : (value > 1.0 ? 1.0 : value);
        }
    }
}
