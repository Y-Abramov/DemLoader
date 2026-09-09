using System;
using System.Collections.Generic;
using System.Text;
using Topomatic.Proj;
using Topomatic.Proj.CoordinateSystems;
using Topomatic.Proj.CoordinateSystems.Transformations;

namespace DemLoader.Robur
{
    /// <summary>Доступ к реестру систем координат Robur и преобразование между
    /// выбранной СК проекта и географическими координатами WGS-84.
    ///
    /// Сигнатуры сверены рефлексией по установленному Robur SDK 16.0.62.12
    /// (Task 5, 2026-09-05): у GeographicCoordinateSystem нет свойства Datum,
    /// как предполагал черновик плана - есть HorizontalDatum (унаследовано от
    /// HorizontalCoordinateSystem). CreateFromCoordinateSystems и
    /// MathTransform.Transform(XY) совпали с планом как есть.
    ///
    /// Почему СК спрашивается у пользователя, а не читается из проекта: публичного API
    /// «СК проекта» в Robur нет, единственный реализатор ICRSSettings - обфусцированный
    /// внутренний тип в Topomatic.Proj.Controller.</summary>
    internal static class CoordSystem
    {
        /// <summary>Все горизонтальные СК реестра, включая МСК.</summary>
        public static IList<HorizontalCoordinateSystem> All()
        {
            var engine = ProjEngine.Current;
            if (engine == null)
                throw new CoordSystemException("Robur не отдал реестр систем координат (ProjEngine недоступен).");
            var list = new List<HorizontalCoordinateSystem>(engine.HorizontalCoordinateSystems);
            if (list.Count == 0)
                throw new CoordSystemException("Реестр систем координат Robur пуст.");
            return list;
        }

        /// <summary>СК по её идентификатору. Идентификатор хранится в задании загрузки,
        /// чтобы dem_update работал в той же СК, что и первая загрузка.</summary>
        public static HorizontalCoordinateSystem ById(Guid id)
        {
            var cs = ProjEngine.Current.GetHorizontalCoordinateSystem(id);
            if (cs == null)
                throw new CoordSystemException(
                    "Система координат из задания загрузки не найдена в реестре Robur (идентификатор " + id + ").");
            return cs;
        }

        /// <summary>Преобразование «выбранная СК -> WGS-84 (широта, долгота)» и обратное.
        /// Строится один раз на загрузку: WarpGrid дёргает его по узлам редкой сетки.</summary>
        public static CoordTransform Create(HorizontalCoordinateSystem projectCs)
        {
            if (projectCs == null) throw new CoordSystemException("Система координат не выбрана.");

            GeographicCoordinateSystem wgs84 = FindWgs84();

            // CoordinateTransformationFactory - статический класс (сверено рефлексией),
            // экземпляр не создаётся.
            CoordinateTransformation toWgs = CoordinateTransformationFactory.CreateFromCoordinateSystems(projectCs, wgs84);
            if (toWgs == null)
                throw new CoordSystemException(
                    "Robur не даёт преобразование из «" + projectCs.Name + "» в WGS-84. " +
                    "Выберите другую систему координат.");

            CoordinateTransformation fromWgs = CoordinateTransformationFactory.CreateFromCoordinateSystems(wgs84, projectCs);
            if (fromWgs == null)
                throw new CoordSystemException(
                    "Robur не даёт обратное преобразование из WGS-84 в «" + projectCs.Name + "».");

            return new CoordTransform(toWgs, fromWgs, projectCs.Name);
        }

        // Живой гейт Task 13 (2026-09-05): поиск по имени датума ("WGS"+"84") не нашёл
        // геосистему в реальном реестре - рефлексия по SDK показывает только сигнатуры
        // API, не реальные данные (та же ловушка, что уже была на AbrBasemap). EPSG:4326 -
        // language/format-независимый идентификатор WGS84, надёжнее сравнения имён.
        private static GeographicCoordinateSystem FindWgs84()
        {
            var candidates = ProjEngine.Current.GeographicCoordinateSystems;

            foreach (var gcs in candidates)
            {
                if (gcs.Authority != null &&
                    string.Equals(gcs.Authority.Name, "EPSG", StringComparison.OrdinalIgnoreCase) &&
                    gcs.Authority.Code == "4326")
                    return gcs;
            }

            // Запасной путь по имени датума - вдруг у встроенной СК Authority не заполнен.
            foreach (var gcs in candidates)
            {
                if (gcs.HorizontalDatum != null && gcs.HorizontalDatum.Name != null &&
                    gcs.HorizontalDatum.Name.IndexOf("WGS", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    gcs.HorizontalDatum.Name.IndexOf("84", StringComparison.Ordinal) >= 0)
                    return gcs;
            }

            // Ни один путь не сработал - перечисляем реестр целиком в тексте отказа,
            // чтобы диагностировать без ещё одного отдельного живого прогона.
            var dump = new StringBuilder();
            dump.Append("В реестре систем координат Robur не найдена WGS-84 (EPSG:4326). Геосистемы в реестре (")
                .Append(candidates.Length).Append("): ");
            foreach (var gcs in candidates)
            {
                dump.Append('"').Append(gcs.Name).Append('"');
                if (gcs.HorizontalDatum != null) dump.Append(" [датум: ").Append(gcs.HorizontalDatum.Name).Append(']');
                if (gcs.Authority != null) dump.Append(" [").Append(gcs.Authority.Name).Append(':').Append(gcs.Authority.Code).Append(']');
                dump.Append("; ");
            }
            throw new CoordSystemException(dump.ToString());
        }
    }

    /// <summary>Пара преобразований в обе стороны плюс имя СК для сообщений об ошибках.</summary>
    internal sealed class CoordTransform
    {
        private readonly CoordinateTransformation _toWgs;
        private readonly CoordinateTransformation _fromWgs;

        public string CsName { get; private set; }

        public CoordTransform(CoordinateTransformation toWgs, CoordinateTransformation fromWgs, string csName)
        {
            _toWgs = toWgs;
            _fromWgs = fromWgs;
            CsName = csName;
        }

        /// <summary>Координаты проекта -> широта и долгота в градусах.
        /// MathTransform.Transform(XY) не существует (сверено рефлексией) - только
        /// Transform(double, double), возвращающий XY.</summary>
        public void ToLatLon(double x, double y, out double lat, out double lon)
        {
            var p = _toWgs.MathTransform.Transform(x, y);
            lon = p.X;
            lat = p.Y;
        }

        /// <summary>Широта и долгота в градусах -> координаты проекта.</summary>
        public void FromLatLon(double lat, double lon, out double x, out double y)
        {
            var p = _fromWgs.MathTransform.Transform(lon, lat);
            x = p.X;
            y = p.Y;
        }
    }
}
