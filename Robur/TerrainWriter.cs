using System;
using System.Collections.Generic;
using Topomatic.ApplicationPlatform.Core;
using Topomatic.ApplicationPlatform.Plugins;
using Topomatic.Cad.Foundation;
using Topomatic.Dtm;
using Topomatic.Sfc;
using DemLoader.Core.Surface;

namespace DemLoader.Robur
{
    /// <summary>Точка результата конвейера: полные координаты проекта и высота.</summary>
    internal struct DemPoint
    {
        public double X;
        public double Y;
        public double Z;

        public DemPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>Создание и наполнение модели ЦММ.
    ///
    /// КРИТИЧНО: методы записи Topomatic.Sfc залицензированы и при отсутствии фичи
    /// МОЛЧА не делают ничего (Surface.Points.Add начинается с проверки лицензии и
    /// выходит без исключения). Поэтому после каждой пакетной записи счётчики
    /// сверяются с ожидаемыми - иначе в дереве проекта появится пустая модель без
    /// единого сообщения об ошибке.
    ///
    /// ДВЕ НАХОДКИ СПАЙКА TASK 2 (docs/superpowers/spikes/2026-09-05-robur-terrain-model-write.md),
    /// без которых код молча не работает или молча теряет данные:
    /// 1. node.Model остаётся null после LockWrite() до вызова LockRead() - без исключения.
    /// 2. surface.BeginUpdate() без парного EndUpdate() - запись видна в текущей сессии
    ///    (все счётчики совпадают, поверхность рисуется), но не переживает Save/перезапуск.
    ///
    /// ЖИВЬЁМ ПРОВЕРЕН сценарий спайка Task 2 (создание, запись, Remove, атрибуты, перезапуск);
    /// полный гейт конвейера - Task 13.</summary>
    internal static class TerrainWriter
    {
        /// <summary>Создаёт модель ЦММ рядом с указанным узлом и наполняет её.</summary>
        public static IProjectModel Create(IProjectModel parent, string name,
                                          IList<DemPoint> points, int nx, int ny, int[] map)
        {
            if (parent == null) throw new ArgumentNullException("parent");
            if (points == null || points.Count == 0)
                throw new TerrainWriteException("Нет ни одной точки для построения поверхности.");

            IProjectModel node = PluginCoreOps.CreateModel(parent, TerrainModel.MODEL_TYPE, name);
            if (node == null)
                throw new TerrainWriteException("Robur не создал модель цифровой модели местности.");

            Fill(node, points, nx, ny, map);
            return node;
        }

        /// <summary>Заменяет содержимое поверхности существующей модели.</summary>
        public static void Replace(IProjectModel node, IList<DemPoint> points, int nx, int ny, int[] map)
        {
            if (node == null) throw new ArgumentNullException("node");
            Fill(node, points, nx, ny, map);
        }

        /// <summary>Живой гейт Task 13: PluginCoreOps.FindFolderModel(node) не находит родителя,
        /// если модель создана прямо в КОРНЕ проекта - root.Uri (путь к .rbprojx) не совпадает
        /// буквально с node.Uri.DirectoryUri (путь к папке), на котором строится сравнение внутри
        /// FindFolderModel. Родитель ищется рекурсивным обходом дерева от явно переданного корня -
        /// тот же приём, что уже проверен в dem_spike_check (FindByNameRecursive).</summary>
        public static void Erase(IProjectModel root, IProjectModel node)
        {
            if (root == null) throw new ArgumentNullException("root");
            if (node == null) throw new ArgumentNullException("node");

            IProjectModel parent = FindParent(root, node);
            if (parent == null)
                throw new TerrainWriteException("Не найден родительский узел модели - удаление отменено.");
            parent.Remove(node, true);
        }

        private static IProjectModel FindParent(IProjectModel node, IProjectModel target)
        {
            if (node == null) return null;

            IProjectModel[] children;
            try { children = node.GetChilds(); }
            catch (NullReferenceException) { return null; }
            if (children == null) return null;

            foreach (var child in children)
            {
                if (child != null && child.Uri != null && target.Uri != null &&
                    child.Uri.AsAbsoluteUri.Equals(target.Uri.AsAbsoluteUri, StringComparison.OrdinalIgnoreCase))
                    return node;

                IProjectModel found;
                try { found = FindParent(child, target); }
                catch (NullReferenceException) { found = null; }
                if (found != null) return found;
            }
            return null;
        }

        private static void Fill(IProjectModel node, IList<DemPoint> points, int nx, int ny, int[] map)
        {
            node.LockWrite();
            try
            {
                // node.Model остаётся null до LockRead() - подтверждено спайком, без
                // исключения, просто тихий null дальше по каскаду проверок.
                node.LockRead();

                var terrain = node.Model as TerrainModel;
                if (terrain == null)
                    throw new TerrainWriteException("Модель проекта не является цифровой моделью местности.");

                Surface surface = terrain.Surface;
                if (surface == null)
                    throw new TerrainWriteException("У модели местности нет поверхности.");

                surface.BeginUpdate();
                try
                {
                    surface.Points.Clear();
                    surface.Triangles.Clear();
                    if (surface.Points.Count != 0)
                        throw new TerrainWriteException(
                            "Robur не дал очистить точки поверхности (проверьте лицензию на работу с моделью местности).");

                    foreach (DemPoint p in points)
                        surface.Points.Add(new SurfacePoint(new Vector3D(p.X, p.Y, p.Z)));

                    if (surface.Points.Count != points.Count)
                        throw new TerrainWriteException(
                            "Robur записал " + surface.Points.Count + " точек из " + points.Count +
                            " (проверьте лицензию на работу с моделью местности).");

                    Triangulate(surface, nx, ny, map);
                }
                finally
                {
                    // Без EndUpdate() запись видна в текущей сессии (все счётчики совпадают,
                    // поверхность рисуется на плане), но не переживает Save/перезапуск Robur -
                    // самая дорогая находка спайка, ни одна проверка в моменте её не ловит.
                    surface.EndUpdate();
                }

                surface.Invalidate();
                surface.Regen();
            }
            finally
            {
                node.UnlockWrite();
            }
            node.Save(false);
        }

        // Триангуляция в локальных координатах: GridMesh.ToLocal сдвигает координаты
        // к локальному началу (float внутри триангулятора Robur иначе склеивает соседние
        // узлы 30-метровой сетки на координатах МСК). DynamicCachedBuilder (Делоне общего
        // назначения) НЕ используется - живой спайк показал, что на регулярной сетке он
        // добавляет служебные вершины сверх переданных точек и валит Invalidate/Regen
        // ArgumentOutOfRangeException. GridMesh.BuildTriangles - свой код (Task 4),
        // гарантирует индексы 1:1 со списком точек по построению.
        private static void Triangulate(Surface surface, int nx, int ny, int[] map)
        {
            IList<MeshTriangle> triangles = GridMesh.BuildTriangles(map, nx, ny);

            if (triangles.Count == 0)
                throw new TerrainWriteException(
                    "Триангуляция не дала ни одного треугольника: проверьте, что участок не вырожден.");

            foreach (MeshTriangle t in triangles)
            {
                var st = new SurfaceTriangle();
                st.A = t.A;
                st.B = t.B;
                st.C = t.C;
                surface.Triangles.Add(st);
            }

            if (surface.Triangles.Count != triangles.Count)
                throw new TerrainWriteException(
                    "Robur записал " + surface.Triangles.Count + " треугольников из " + triangles.Count +
                    " (проверьте лицензию на работу с моделью местности).");
        }
    }
}
