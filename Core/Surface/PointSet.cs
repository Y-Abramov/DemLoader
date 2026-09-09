using System;
using System.Collections.Generic;

namespace DemLoader.Core.Surface
{
    /// <summary>Копит узлы поверхности с фильтрами отбраковки и одноразовым сдвигом Z.
    ///
    /// Счётчики принятых/отброшенных точек ведутся здесь же, в одном месте: без этого отчёт
    /// пользователю ("N точек принято, M вне контура, K без данных") пришлось бы заново
    /// выводить постфактум из списка точек, а часть причин отбраковки к этому моменту уже
    /// потеряна.</summary>
    internal sealed class PointSet
    {
        private readonly Func<double, double, bool> _insideContour;
        private readonly double _zShift;
        private readonly List<(double X, double Y, double Z)> _points = new List<(double X, double Y, double Z)>();

        public int Accepted { get; private set; }
        public int SkippedNoData { get; private set; }
        public int SkippedOutside { get; private set; }

        public IReadOnlyList<(double X, double Y, double Z)> Points { get { return _points; } }

        public PointSet(Func<double, double, bool> insideContour, double zShift)
        {
            if (insideContour == null) throw new ArgumentNullException("insideContour");

            _insideContour = insideContour;
            _zShift = zShift;
        }

        public bool TryAdd(double x, double y, double? z)
        {
            // Порядок проверок важен: точка без данных отбраковывается ДО проверки контура -
            // у неё изначально не было шанса попасть в поверхность, и вне контура она или внутри,
            // не имеет значения для причины отказа.
            if (z == null)
            {
                SkippedNoData++;
                return false;
            }

            if (!_insideContour(x, y))
            {
                SkippedOutside++;
                return false;
            }

            _points.Add((x, y, z.Value + _zShift));
            Accepted++;
            return true;
        }
    }
}
