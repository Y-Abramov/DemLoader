using System;
using System.Collections.Generic;
using Topomatic.Cad.Foundation;
using Topomatic.Cad.View;
using Topomatic.Cad.View.Hints;
using Topomatic.Dwg;
using Topomatic.Dwg.Entities;
using Topomatic.Dwg.Layer;

namespace DemLoader.Robur
{
    internal enum AreaKind { Frame, Polyline }

    /// <summary>Выбранная область: вид и геометрия зафиксированы в момент выбора
    /// и дальше не меняются.</summary>
    internal sealed class PickedArea
    {
        public AreaKind Kind { get; private set; }
        public IList<double[]> Loop { get; private set; }

        public PickedArea(AreaKind kind, IList<double[]> loop)
        {
            Kind = kind;
            Loop = loop;
        }
    }

    /// <summary>Два способа задания участка (коридор вдоль трассы убран - решение пользователя,
    /// живой гейт Task 13). ЖИВЬЁМ НЕ ПРОВЕРЯЛСЯ за пределами Task 13.</summary>
    internal static class AreaPicker
    {
        /// <summary>Рамка: две точки с резинкой (FrameCursor) - без неё пользователь не видит
        /// рамку во время выбора второго угла (живой гейт Task 13). Возвращает null на Esc.</summary>
        public static PickedArea PickFrame(CadView cadView)
        {
            Vector3D p1;
            if (!CadCursors.GetPoint(cadView, out p1, "Первый угол участка:")) return null;

            var frame = new FrameCursor(cadView, "Второй угол участка:", p1);
            if (frame.GetFrame() != GetPointResult.Accept) return null;

            Vector3D a = frame.FirstPoint;
            Vector3D b = frame.SecondPoint;

            double x1 = Math.Min(a.X, b.X), x2 = Math.Max(a.X, b.X);
            double y1 = Math.Min(a.Y, b.Y), y2 = Math.Max(a.Y, b.Y);
            if (x2 - x1 < 1.0 || y2 - y1 < 1.0) return null;

            var loop = new List<double[]>
            {
                new double[] { x1, y1 },
                new double[] { x2, y1 },
                new double[] { x2, y2 },
                new double[] { x1, y2 }
            };
            return new PickedArea(AreaKind.Frame, loop);
        }

        /// <summary>Замкнутая полилиния чертежа. Незамкнутая не предлагается к выбору
        /// вовсе - фильтр стоит в самом выборе.</summary>
        public static PickedArea PickPolyline(CadView cadView)
        {
            var layer = DrawingLayer.GetDrawingLayer(cadView);
            if (layer == null) return null;

            var picked = layer.SelectOneEntity(
                e => { var pl = e as DwgPolyline; return pl != null && pl.Closed; },
                "Выберите замкнутую полилинию контура участка:") as DwgPolyline;
            if (picked == null) return null;

            // DwgPolyline не имеет свойства Vertices (сверено рефлексией SDK 16.0.62.12
            // и Dev Guide) - вершины отдаёт ConvertToPosArray(IList<Vector2D>).
            var positions = new List<Vector2D>();
            picked.ConvertToPosArray(positions);
            if (positions.Count < 3) return null;

            var loop = new List<double[]>(positions.Count);
            foreach (var p in positions) loop.Add(new double[] { p.X, p.Y });

            return new PickedArea(AreaKind.Polyline, loop);
        }
    }
}
