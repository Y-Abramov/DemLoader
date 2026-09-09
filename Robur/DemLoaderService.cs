using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Topomatic.ApplicationPlatform.Core;
using DemLoader.Core.Dem;
using DemLoader.Core.Geo;
using DemLoader.Core.Job;
using DemLoader.Core.Net;
using DemLoader.Core.Sources;
using DemLoader.Core.Surface;

namespace DemLoader.Robur
{
    /// <summary>Что построить. Область приходит уже выбранной (AreaPicker, Task 6) - диалог
    /// закрыт к моменту запуска сервиса, повторно указывать объект в чертеже нельзя.</summary>
    internal sealed class DemBuildRequest
    {
        public DemSource Source;
        public PickedArea Area;

        public double Step;
        public double ZShift;
        public string SurfaceName;

        /// <summary>Идентификатор выбранной СК Robur (HorizontalCoordinateSystem.Id) - хранится
        /// в задании, чтобы dem_update пересчитал в той же СК.</summary>
        public Guid CoordSystemId;

        /// <summary>Личный ключ OpenTopography из настроек модуля. Пусто - источники с
        /// RequiresKey отказывают с объяснением.</summary>
        public string ApiKey;

        /// <summary>Узел дерева проекта, рядом с которым создаётся новая модель ЦММ.</summary>
        public IProjectModel Parent;

        public int MaxPoints = DemLoaderService.DefaultMaxPoints;
        public int TimeoutMs = DemLoaderService.DefaultTimeoutMs;
        public string CacheRoot = DemCache.DefaultRoot;
    }

    internal sealed class BuildResult
    {
        public bool Ok;
        public string Error;

        public IProjectModel Node;
        public string SurfaceName;
        public double Step;
        public int Accepted;
        public int SkippedNoData;
        public int SkippedOutside;
        public readonly List<string> MissingTiles = new List<string>();

        /// <summary>Строка для итогового сообщения: сколько точек легло в поверхность и почему
        /// остальные не легли. Без неё «поверхность построена» ничего не говорит о том, накрыл
        /// ли источник участок целиком.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("Поверхность \"").Append(SurfaceName).Append("\": ");
            text.Append(Accepted.ToString(CultureInfo.InvariantCulture)).Append(" точек, шаг ");
            text.Append(Step.ToString("0.##", CultureInfo.InvariantCulture)).Append(" м.");

            if (SkippedNoData > 0)
                text.Append(" Без данных источника: ").Append(SkippedNoData.ToString(CultureInfo.InvariantCulture)).Append('.');

            if (SkippedOutside > 0)
                text.Append(" Вне контура: ").Append(SkippedOutside.ToString(CultureInfo.InvariantCulture)).Append('.');

            if (MissingTiles.Count > 0)
                text.Append(" У источника нет тайлов: ").Append(string.Join(", ", MissingTiles.ToArray())).Append('.');

            return text.ToString();
        }
    }

    /// <summary>Что получилось у dem_export: файлы источника как есть, без построения поверхности.</summary>
    internal sealed class ExportResult
    {
        public bool Ok;
        public string Error;

        public readonly List<string> Files = new List<string>();

        /// <summary>Клетки, которых у источника не оказалось (над океаном - штатно), с причиной.</summary>
        public readonly List<string> Missing = new List<string>();

        public string AttributionPath;
        public long TotalBytes;

        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("Сохранено файлов: ").Append(Files.Count.ToString(CultureInfo.InvariantCulture));
            text.Append(", всего ").Append((TotalBytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture)).Append(" МБ.");

            if (!string.IsNullOrEmpty(AttributionPath))
                text.Append(" Атрибуция источника: ").Append(Path.GetFileName(AttributionPath)).Append('.');

            if (Missing.Count > 0)
                text.Append(" Не получены: ").Append(string.Join(", ", Missing.ToArray())).Append('.');

            return text.ToString();
        }
    }

    /// <summary>Сценарий целиком: СК -> габарит в градусах -> план тайлов -> байтовые источники ->
    /// DemGrid -> сетка GridPlan -> точки через WarpGrid -> TerrainWriter.
    ///
    /// Перенос AbrCivil.DemLoader.Cad.DemLoaderService (civil3d/demloader/Cad/DemLoaderService.cs) -
    /// тот же конвейер и та же обработка отказов, адаптированные под Robur:
    /// - вместо Database/Transaction/SurfaceBuilder (AutoCAD TIN) - IProjectModel/TerrainWriter
    ///   (Sfc API, Task 8) с триангуляцией через GridMesh по регулярной сетке (не облако точек);
    /// - вместо PickedContour.Contains (делегат из чертёжного слоя Civil) - Polygon.Contains
    ///   (Task 9, чистая геометрия, тестируется без Robur);
    /// - вместо CoordSystemService.ToLonLat - CoordTransform.ToLatLon (Task 5). ПОРЯДОК ВЫХОДНЫХ
    ///   ПАРАМЕТРОВ ОБРАТНЫЙ: ToLatLon даёт (lat, lon), Civil-версия ждала (lon, lat) - при
    ///   переносе легко перепутать местами.
    ///
    /// Общая часть конвейера (габарит -> тайлы -> сетка -> точки) вынесена в ComputeSample
    /// (Task 10) - Run (создание) и RunReplace (обновление, dem_update) отличаются только тем,
    /// откуда берут контур/параметры (диалог либо сохранённое задание) и последним шагом
    /// (TerrainWriter.Create либо .Replace).
    ///
    /// ЖИВЬЁМ НЕ ПРОВЕРЯЛСЯ - полный гейт конвейера Task 13.</summary>
    internal sealed class DemLoaderService
    {
        // Значения дублируют DemSettings (Core, виден тестам) под именами, которыми пользуется
        // Robur-слой - см. предупреждение в DemSettings о недублировании чисел.
        public const int DefaultMaxPoints = DemSettings.DefaultMaxPoints;
        public const int DefaultTimeoutMs = DemSettings.DefaultTimeoutMs;

        /// <summary>Узлов сетки соответствия «координаты проекта - градусы» по стороне. Родное
        /// преобразование Robur вызывается только в узлах: на сотнях тысяч точек вызов на каждую
        /// означал бы столько же переходов через CoordTransform.</summary>
        private const int WarpNodes = 32;

        /// <summary>Как часто обновляется прогресс при выборке отметок.</summary>
        private const int ProgressEveryRows = 8;

        private readonly Action<int, int, string> _progress;

        public DemLoaderService(Action<int, int, string> progress)
        {
            _progress = progress ?? delegate { };
        }

        public BuildResult Run(DemBuildRequest request, CoordTransform cs, CancellationToken token)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (cs == null) throw new ArgumentNullException("cs");

            if (request.Parent == null)
                return Fail("Не выбран узел дерева проекта для новой модели.");

            var area = request.Area;
            if (area == null || area.Loop == null || area.Loop.Count < 3)
                return Fail("Область не задана или контур повреждён.");

            var outcome = ComputeSample(area.Loop, request.Source, request.Step, request.ZShift,
                request.ApiKey, request.MaxPoints, request.TimeoutMs, request.CacheRoot, cs, token);
            if (!outcome.Ok) return Fail(outcome.Error);

            Report(_progress, 0, 1, "Построение поверхности");

            var job = new DemJob
            {
                SourceId = request.Source.Id,
                CoordSystemId = request.CoordSystemId.ToString(),
                Step = outcome.Plan.Step,
                ZShift = request.ZShift,
                SurfaceName = request.SurfaceName,
                AreaKind = AreaKindOf(area.Kind),
                CreatedUtc = DemJob.Stamp(DateTime.UtcNow)
            };
            Bounds(area.Loop, out job.MinX, out job.MinY, out job.MaxX, out job.MaxY);
            foreach (var p in area.Loop)
            {
                job.ContourPointsX.Add(p[0]);
                job.ContourPointsY.Add(p[1]);
            }

            // Последняя проверка отмены, вплотную к точке невозврата: между ней и TerrainWriter.Create
            // нет ни одного вызова, качающего очередь сообщений (см. ProgressDialog).
            token.ThrowIfCancellationRequested();

            IProjectModel node;
            try
            {
                node = TerrainWriter.Create(request.Parent, request.SurfaceName, outcome.Points,
                    outcome.Plan.Columns, outcome.Plan.Rows, outcome.Map);
            }
            catch (TerrainWriteException error)
            {
                return Fail(error.Message);
            }

            JobStore.Save(node, job);
            Report(_progress, 1, 1, "Готово");

            return Success(node, job.SurfaceName, outcome);
        }

        /// <summary>Перекачка по сохранённому заданию (dem_update): контур, источник, шаг и
        /// поправка Z берутся из job, не из диалога - dem_update ничего не спрашивает у
        /// пользователя, кроме какую из своих моделей обновить (ModelPickDialog). Новая
        /// поверхность считается ЦЕЛИКОМ и только потом подменяет старую (TerrainWriter.Replace
        /// вызывается последним) - отмена или отказ до этой точки оставляют прежнюю поверхность
        /// нетронутой.</summary>
        public BuildResult RunReplace(DemJob job, IList<DemSource> sources, CoordTransform cs,
                                      IProjectModel target, string apiKey, int maxPoints,
                                      int timeoutMs, string cacheRoot, CancellationToken token)
        {
            if (job == null) throw new ArgumentNullException("job");
            if (cs == null) throw new ArgumentNullException("cs");
            if (target == null) throw new ArgumentNullException("target");

            DemSource source = FindSource(sources, job.SourceId);
            if (source == null)
                return Fail("Источник \"" + job.SourceId + "\" из сохранённого задания не найден в текущем каталоге источников.");

            if (job.ContourPointsX.Count != job.ContourPointsY.Count || job.ContourPointsX.Count < 3)
                return Fail("Контур в сохранённом задании повреждён.");

            var loop = new List<double[]>(job.ContourPointsX.Count);
            for (int i = 0; i < job.ContourPointsX.Count; i++)
                loop.Add(new double[] { job.ContourPointsX[i], job.ContourPointsY[i] });

            var outcome = ComputeSample(loop, source, job.Step, job.ZShift, apiKey, maxPoints,
                timeoutMs, cacheRoot, cs, token);
            if (!outcome.Ok) return Fail(outcome.Error);

            Report(_progress, 0, 1, "Обновление поверхности");

            // Точка невозврата - до неё прежняя поверхность цела независимо от того, что
            // случилось выше (сеть, лицензия, отмена).
            token.ThrowIfCancellationRequested();

            try
            {
                TerrainWriter.Replace(target, outcome.Points, outcome.Plan.Columns, outcome.Plan.Rows, outcome.Map);
            }
            catch (TerrainWriteException error)
            {
                return Fail(error.Message);
            }

            job.Step = outcome.Plan.Step;
            job.CreatedUtc = DemJob.Stamp(DateTime.UtcNow);
            JobStore.Save(target, job);
            Report(_progress, 1, 1, "Готово");

            return Success(target, job.SurfaceName, outcome);
        }

        /// <summary>Файлы источника на область, как есть, в выбранную папку - для dem_export.
        /// Ни сетки точек, ни поверхности: GridPlan/PointSet/TerrainWriter здесь не участвуют
        /// вовсе, смысл команды именно в исходных данных файлом для стороннего ГИС-инструмента.
        ///
        /// Перенос AbrCivil.DemLoader.Cad.DemLoaderService.Export - список уже скачанных файлов
        /// накапливается ПО ХОДУ (не собирается в конце): обрыв на N-м файле не теряет из отчёта
        /// первые N-1, уже лежащие на диске.</summary>
        public ExportResult Export(IList<double[]> loop, DemSource source, CoordTransform cs,
                                   string apiKey, string folder, int timeoutMs, CancellationToken token)
        {
            if (cs == null) throw new ArgumentNullException("cs");
            if (string.IsNullOrEmpty(folder)) return ExportFail("Папка для файлов не выбрана.");
            if (source == null) return ExportFail("Источник данных не выбран.");
            if (loop == null || loop.Count < 3) return ExportFail("Область не задана или контур повреждён.");

            if (source.RequiresKey && string.IsNullOrEmpty(apiKey))
                return ExportFail("Источник \"" + source.Name + "\" работает только по личному ключу. " +
                                  "Ключ вводится в настройках модуля (dem_settings), регистрация бесплатная: " +
                                  OpenTopoRequest.SignUpUrl);

            GeoBox box;
            try
            {
                box = ToGeoBox(loop, cs);
            }
            catch (Exception error)
            {
                return ExportFail("Не удалось пересчитать контур из системы координат \"" + cs.CsName +
                                  "\" в широту и долготу: " + error.Message);
            }

            var result = new ExportResult();
            var transport = new HttpTileTransport(timeoutMs);

            try
            {
                Directory.CreateDirectory(folder);

                string failure = string.IsNullOrEmpty(source.OpenTopoType)
                    ? ExportTiles(source, box, transport, folder, result, _progress, token)
                    : ExportOpenTopo(source, box, transport, apiKey, folder, result, _progress, token);

                if (failure != null) return ExportFail(failure);
                if (result.Files.Count == 0) return ExportFail(NoData(source, box, result.Missing));

                result.AttributionPath = WriteAttribution(folder, source, box, result);
            }
            // Отказ записи мог случиться на N-м файле из десяти - предыдущие уже лежат на диске
            // целиком (WriteWhole пишет через .part). Возвращается ТОТ ЖЕ result со списком
            // записанного, а не свежий отказ: иначе пользователь получил бы "не удалось" и
            // гигабайты безымянных для него файлов в папке без единого слова, что это такое.
            catch (IOException error)
            {
                return ExportFailed(result, "Не удалось записать файлы в папку " + folder + ": " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                return ExportFailed(result, "Нет прав на запись в папку " + folder + ": " + error.Message);
            }

            result.Ok = true;
            return result;
        }

        /// <summary>Copernicus: клетки целиком. Отказа наружу нет - непришедшая клетка попадает
        /// в Missing, остальные качаются дальше (тот же порядок, что в LoadTiles).</summary>
        private static string ExportTiles(DemSource source, GeoBox box, ITileTransport transport, string folder,
                                          ExportResult result, Action<int, int, string> progress, CancellationToken token)
        {
            var plan = DemTilePlan.Create(box, source.ResolutionMarker);

            for (int i = 0; i < plan.Tiles.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                Report(progress, i, plan.Tiles.Count, "Загрузка файлов источника");

                var tile = plan.Tiles[i];
                string url = DemTileKey.UrlOfCell(source.BaseUrl, tile.Lat, tile.Lon, source.ResolutionMarker);

                byte[] data;
                try
                {
                    data = transport.Get(url, source.UserAgent, token);
                }
                catch (WebException error)
                {
                    result.Missing.Add(tile.Name + " (" + error.Message + ")");
                    continue;
                }

                string path = Path.Combine(folder, tile.Name + ".tif");
                WriteWhole(path, data);

                result.Files.Add(path);
                result.TotalBytes += data.LongLength;
            }

            Report(progress, plan.Tiles.Count, plan.Tiles.Count, "Загрузка файлов источника");
            return null;
        }

        /// <summary>OpenTopography: один файл на габарит - отказ фатален, делить нечего.</summary>
        private static string ExportOpenTopo(DemSource source, GeoBox box, ITileTransport transport, string apiKey,
                                             string folder, ExportResult result, Action<int, int, string> progress,
                                             CancellationToken token)
        {
            Report(progress, 0, 1, "Запрос к OpenTopography");

            byte[] data;
            try
            {
                data = transport.Get(OpenTopoRequest.Build(source.OpenTopoType, box, apiKey), source.UserAgent, token);
            }
            catch (WebException error)
            {
                return Translate(source, box, error).Message;
            }

            string path = Path.Combine(folder, OpenTopoFileName(source.OpenTopoType, box) + ".tif");
            WriteWhole(path, data);

            result.Files.Add(path);
            result.TotalBytes += data.LongLength;

            Report(progress, 1, 1, "Запрос к OpenTopography");
            return null;
        }

        private static void WriteWhole(string path, string text)
        {
            // С BOM: файл открывают Блокнотом, и без метки кодировки кириллица в нём читается
            // как мусор на системах с кодовой страницей по умолчанию.
            WriteWhole(path, new UTF8Encoding(true).GetBytes(text));
        }

        /// <summary>Сначала во временный файл, потом переименование - оборванная запись не должна
        /// остаться под именем готового файла и уехать в ГИС как полные данные.</summary>
        private static void WriteWhole(string path, byte[] data)
        {
            string partial = path + ".part";
            File.WriteAllBytes(partial, data);
            if (File.Exists(path)) File.Delete(path);
            File.Move(partial, path);
        }

        /// <summary>Кто правообладатель, что это за данные и от чего считаются высоты. Файл
        /// кладётся рядом с данными, потому что через месяц эти сведения будут нужны там же, где
        /// лежит .tif, а не в переписке.</summary>
        private static string WriteAttribution(string folder, DemSource source, GeoBox box, ExportResult result)
        {
            var c = CultureInfo.InvariantCulture;
            var text = new StringBuilder();

            text.AppendLine("Данные рельефа, выгруженные модулем ABR | Загрузка рельефа (dem_export).");
            text.AppendLine();
            text.AppendLine("Источник: " + source.Name + " (" + source.Id + ")");

            if (!string.IsNullOrEmpty(source.Copyright))
                text.AppendLine("Правообладатель: " + source.Copyright);

            text.AppendLine("Вид данных: " + (string.IsNullOrEmpty(source.SurfaceKind) ? "DSM" : source.SurfaceKind) +
                            ", вертикальная основа: " + (string.IsNullOrEmpty(source.VerticalDatum) ? "EGM2008" : source.VerticalDatum));

            if (source.StepMetres > 0.0)
                text.AppendLine("Разрешение источника: " + source.StepMetres.ToString("0.##", c) + " м");

            text.AppendLine("Система координат файлов: географическая, WGS84 (EPSG:4326) - как их отдаёт источник, " +
                            "без пересчёта в систему координат проекта.");
            text.AppendLine("Область: широта " + box.MinLat.ToString("F4", c) + ".." + box.MaxLat.ToString("F4", c) +
                            ", долгота " + box.MinLon.ToString("F4", c) + ".." + box.MaxLon.ToString("F4", c));
            text.AppendLine("Выгружено: " + DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm", c) + " UTC");
            text.AppendLine();
            text.AppendLine("Файлы:");
            foreach (string file in result.Files) text.AppendLine("  " + Path.GetFileName(file));

            if (result.Missing.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Не получены (у источника нет данных на эту клетку либо отказ сети):");
                foreach (string missing in result.Missing) text.AppendLine("  " + missing);
            }

            text.AppendLine();
            text.AppendLine("Условия использования данных определяет их правообладатель. Ссылка на источник " +
                            "обязательна при публикации и в проектной документации.");

            string path = Path.Combine(folder, "Источник данных.txt");
            WriteWhole(path, text.ToString());
            return path;
        }

        private static ExportResult ExportFail(string error)
        {
            return new ExportResult { Ok = false, Error = error };
        }

        private static ExportResult ExportFailed(ExportResult result, string error)
        {
            result.Ok = false;
            result.Error = error;
            return result;
        }

        // ---------- общий конвейер ----------

        /// <summary>Промежуточный итог: набор точек регулярной сетки и карта их индексов
        /// (GridMesh.BuildTriangles, Task 4), либо текст отказа. И Run, и RunReplace приводят
        /// свои разнородные входы (диалог / сохранённое задание) к одному и тому же вызову.</summary>
        private sealed class SampleOutcome
        {
            public bool Ok;
            public string Error;
            public GridPlan Plan;
            public List<DemPoint> Points;
            public int[] Map;
            public readonly List<string> MissingTiles = new List<string>();
            public int Accepted;
            public int SkippedNoData;
            public int SkippedOutside;
        }

        private SampleOutcome ComputeSample(IList<double[]> loop, DemSource source, double step, double zShift,
                                            string apiKey, int maxPoints, int timeoutMs, string cacheRoot,
                                            CoordTransform cs, CancellationToken token)
        {
            var outcome = new SampleOutcome();

            if (source == null) { outcome.Error = "Источник данных не выбран."; return outcome; }
            if (loop == null || loop.Count < 3) { outcome.Error = "Область не задана или контур повреждён."; return outcome; }

            if (source.RequiresKey && string.IsNullOrEmpty(apiKey))
            {
                outcome.Error = "Источник \"" + source.Name + "\" работает только по личному ключу. " +
                                "Ключ выдаётся бесплатно после регистрации: " + OpenTopoRequest.SignUpUrl;
                return outcome;
            }

            double minX, minY, maxX, maxY;
            Bounds(loop, out minX, out minY, out maxX, out maxY);
            if (maxX - minX <= 0.0 || maxY - minY <= 0.0)
            {
                outcome.Error = "Область имеет нулевой размер.";
                return outcome;
            }

            // Габарит в градусах берётся по ВСЕМ узлам контура, а не по четырём углам габарита в
            // проекте: в проекции стороны прямоугольника не остаются прямыми, и угловой габарит
            // не накрывает середины сторон.
            GeoBox box;
            try
            {
                box = ToGeoBox(loop, cs);
            }
            catch (Exception error)
            {
                outcome.Error = "Не удалось пересчитать контур из системы координат \"" + cs.CsName +
                                "\" в широту и долготу: " + error.Message;
                return outcome;
            }

            var missing = new List<string>();
            DemGrid grid;

            var transport = new HttpTileTransport(timeoutMs);

            try
            {
                grid = string.IsNullOrEmpty(source.OpenTopoType)
                    ? LoadTiles(source, box, transport, cacheRoot, missing, _progress, token)
                    : LoadOpenTopo(source, box, transport, cacheRoot, apiKey, _progress, token);
            }
            catch (DemTileMissingException error)
            {
                // Сюда долетает только отсутствие ЕДИНСТВЕННОГО объекта у OpenTopography:
                // у Copernicus каждая клетка ловится отдельно и попадает в missing.
                outcome.Error = error.Message;
                return outcome;
            }
            catch (NotSupportedException error)
            {
                outcome.Error = "Формат данных источника не поддерживается: " + error.Message;
                return outcome;
            }
            catch (InvalidDataException error)
            {
                outcome.Error = "Файл источника не разобран: " + error.Message;
                return outcome;
            }
            catch (WebException error)
            {
                outcome.Error = Network(source, error);
                return outcome;
            }
            catch (IOException error)
            {
                outcome.Error = "Ошибка чтения с диска (кэш " + cacheRoot + "): " + error.Message;
                return outcome;
            }

            if (grid.TileCount == 0)
            {
                outcome.Error = NoData(source, box, missing);
                return outcome;
            }

            GridPlan plan;
            try
            {
                plan = GridPlan.Create(maxX - minX, maxY - minY, step, source.StepMetres, maxPoints);
            }
            catch (ArgumentException error)
            {
                outcome.Error = "Не удалось спланировать сетку точек: " + error.Message;
                return outcome;
            }
            catch (InvalidOperationException error)
            {
                outcome.Error = "Не удалось спланировать сетку точек: " + error.Message;
                return outcome;
            }

            var warp = SampleWarp(cs, minX, minY, maxX, maxY);
            var points = new PointSet((x, y) => Polygon.Contains(loop, x, y), zShift);
            var map = new int[plan.Columns * plan.Rows];

            try
            {
                Sample(grid, warp, plan, points, minX, minY, map, _progress, token);
            }
            catch (NotSupportedException error)
            {
                outcome.Error = "Блок данных источника не разобран: " + error.Message;
                return outcome;
            }
            catch (InvalidDataException error)
            {
                outcome.Error = "Блок данных источника не разобран: " + error.Message;
                return outcome;
            }
            catch (DemTileMissingException error)
            {
                outcome.Error = error.Message + " Похоже, источник изменился между чтением заголовка и данных.";
                return outcome;
            }
            catch (WebException error)
            {
                outcome.Error = Network(source, error);
                return outcome;
            }
            catch (IOException error)
            {
                outcome.Error = "Ошибка чтения с диска (кэш " + cacheRoot + "): " + error.Message;
                return outcome;
            }

            if (points.Accepted == 0)
            {
                outcome.Error = "Ни одна точка не попала в поверхность: " +
                                points.SkippedNoData.ToString(CultureInfo.InvariantCulture) + " без данных источника, " +
                                points.SkippedOutside.ToString(CultureInfo.InvariantCulture) + " вне контура. " +
                                "Пустую поверхность не создаём - проверьте область и охват источника.";
                return outcome;
            }

            var demPoints = new List<DemPoint>(points.Points.Count);
            foreach (var p in points.Points) demPoints.Add(new DemPoint(p.X, p.Y, p.Z));

            outcome.Ok = true;
            outcome.Plan = plan;
            outcome.Points = demPoints;
            outcome.Map = map;
            outcome.MissingTiles.AddRange(missing);
            outcome.Accepted = points.Accepted;
            outcome.SkippedNoData = points.SkippedNoData;
            outcome.SkippedOutside = points.SkippedOutside;
            return outcome;
        }

        private static BuildResult Success(IProjectModel node, string surfaceName, SampleOutcome outcome)
        {
            var result = new BuildResult
            {
                Ok = true,
                Node = node,
                SurfaceName = surfaceName,
                Step = outcome.Plan.Step,
                Accepted = outcome.Accepted,
                SkippedNoData = outcome.SkippedNoData,
                SkippedOutside = outcome.SkippedOutside
            };
            result.MissingTiles.AddRange(outcome.MissingTiles);
            return result;
        }

        private static DemSource FindSource(IList<DemSource> sources, string id)
        {
            if (sources == null) return null;
            foreach (var s in sources)
                if (s.Id == id) return s;
            return null;
        }

        // ---------- тайлы ----------

        /// <summary>Copernicus: клетки 1x1 градус из бакета. Отсутствующая клетка (404) - «здесь
        /// нет суши», а не отказ: она копится в missing, остальные читаются дальше. Фатально
        /// только когда не открылась ни одна.</summary>
        private static DemGrid LoadTiles(DemSource source, GeoBox box, ITileTransport transport,
                                         string cacheRoot, List<string> missing,
                                         Action<int, int, string> progress, CancellationToken token)
        {
            var plan = DemTilePlan.Create(box, source.ResolutionMarker);
            var cache = new DemCache(cacheRoot);
            var grid = new DemGrid();

            for (int i = 0; i < plan.Tiles.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                Report(progress, i, plan.Tiles.Count, "Загрузка тайлов");

                var tile = plan.Tiles[i];
                string url = DemTileKey.UrlOfCell(source.BaseUrl, tile.Lat, tile.Lon, source.ResolutionMarker);
                var bytes = new HttpTiffSource(url, tile.Name, transport, cache, source.UserAgent);

                try
                {
                    grid.Add(new DemTileReader(bytes));
                }
                catch (DemTileMissingException)
                {
                    missing.Add(tile.Name);
                }
            }

            Report(progress, plan.Tiles.Count, plan.Tiles.Count, "Загрузка тайлов");
            return grid;
        }

        /// <summary>OpenTopography: не бакет клеток, а один GeoTIFF на запрошенный габарит. Ключ
        /// личный - условия сервиса прямо запрещают вшивать ключ в приложение (см.
        /// OpenTopoRequest), поэтому он приходит из настроек пользователя.</summary>
        private static DemGrid LoadOpenTopo(DemSource source, GeoBox box, ITileTransport transport,
                                            string cacheRoot, string apiKey,
                                            Action<int, int, string> progress, CancellationToken token)
        {
            Report(progress, 0, 1, "Запрос к OpenTopography");

            string path = OpenTopoPath(cacheRoot, source.OpenTopoType, box);

            if (!File.Exists(path))
            {
                string url = OpenTopoRequest.Build(source.OpenTopoType, box, apiKey);

                byte[] data;
                try
                {
                    data = transport.Get(url, source.UserAgent, token);
                }
                catch (WebException error)
                {
                    throw Translate(source, box, error);
                }

                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                // Сначала во временный файл, потом переименование: оборванная запись не должна
                // остаться на диске под именем готового файла.
                string partial = path + ".part";
                File.WriteAllBytes(partial, data);
                if (File.Exists(path)) File.Delete(path);
                File.Move(partial, path);
            }

            Report(progress, 1, 1, "Запрос к OpenTopography");

            var grid = new DemGrid();
            grid.Add(new DemTileReader(new FileTiffSource(path)));
            return grid;
        }

        // ---------- выборка ----------

        /// <summary>Узлы регулярной сетки в координатах проекта, отметки - из DEM через сетку
        /// соответствия. Отбраковка (вне контура, нет данных) и поправка Z - на PointSet.
        /// map[cell] - позиция точки в итоговом списке (для GridMesh.BuildTriangles, Task 4)
        /// либо -1, если узел отсеян.</summary>
        private static void Sample(DemGrid grid, WarpGrid warp, GridPlan plan, PointSet points,
                                   double minX, double minY, int[] map,
                                   Action<int, int, string> progress, CancellationToken token)
        {
            int index = 0;
            for (int row = 0; row < plan.Rows; row++)
            {
                token.ThrowIfCancellationRequested();
                if (row % ProgressEveryRows == 0) Report(progress, row, plan.Rows, "Выборка отметок");

                double y = minY + row * plan.Step;

                for (int column = 0; column < plan.Columns; column++)
                {
                    double x = minX + column * plan.Step;
                    int cell = row * plan.Columns + column;

                    double lon, lat;
                    warp.Map(x, y, out lon, out lat);

                    double elevation;
                    bool found = grid.TryGetElevation(lon, lat, out elevation);
                    map[cell] = points.TryAdd(x, y, found ? (double?)elevation : null) ? index++ : -1;
                }
            }

            Report(progress, plan.Rows, plan.Rows, "Выборка отметок");
        }

        private static WarpGrid SampleWarp(CoordTransform cs, double minX, double minY, double maxX, double maxY)
        {
            var warp = new WarpGrid(WarpNodes, WarpNodes, minX, minY, maxX, maxY);

            for (int row = 0; row < WarpNodes; row++)
            {
                double y = minY + (maxY - minY) * row / (WarpNodes - 1);

                for (int column = 0; column < WarpNodes; column++)
                {
                    double x = minX + (maxX - minX) * column / (WarpNodes - 1);

                    // ToLatLon отдаёт (lat, lon) - WarpGrid.SetNode ждёт (lon, lat). Перепутать
                    // местами легко, это и произошло бы при буквальном копировании Civil-кода.
                    double lat, lon;
                    cs.ToLatLon(x, y, out lat, out lon);
                    warp.SetNode(column, row, lon, lat);
                }
            }

            return warp;
        }

        private static GeoBox ToGeoBox(IList<double[]> loop, CoordTransform cs)
        {
            double minLon = double.MaxValue, minLat = double.MaxValue;
            double maxLon = double.MinValue, maxLat = double.MinValue;

            foreach (var p in loop)
            {
                double lat, lon;
                cs.ToLatLon(p[0], p[1], out lat, out lon);

                if (lon < minLon) minLon = lon;
                if (lon > maxLon) maxLon = lon;
                if (lat < minLat) minLat = lat;
                if (lat > maxLat) maxLat = lat;
            }

            return new GeoBox(minLon, minLat, maxLon, maxLat);
        }

        // ---------- общее ----------

        private static void Bounds(IList<double[]> loop, out double minX, out double minY,
                                   out double maxX, out double maxY)
        {
            minX = double.MaxValue; minY = double.MaxValue;
            maxX = double.MinValue; maxY = double.MinValue;

            foreach (var p in loop)
            {
                if (p[0] < minX) minX = p[0];
                if (p[0] > maxX) maxX = p[0];
                if (p[1] < minY) minY = p[1];
                if (p[1] > maxY) maxY = p[1];
            }
        }

        private static string AreaKindOf(AreaKind kind)
        {
            switch (kind)
            {
                case AreaKind.Polyline: return DemJob.KindPolyline;
                default: return DemJob.KindWindow;
            }
        }

        private static string NoData(DemSource source, GeoBox box, List<string> missing)
        {
            var text = new StringBuilder();
            text.Append("В этой области нет данных источника \"").Append(source.Name).Append("\".");

            if (missing.Count > 0)
                text.Append(" Отсутствуют тайлы: ").Append(string.Join(", ", missing.ToArray())).Append('.');

            var c = CultureInfo.InvariantCulture;
            text.Append(" Область: ")
                .Append(box.MinLat.ToString("F3", c)).Append("..").Append(box.MaxLat.ToString("F3", c))
                .Append(" широты, ")
                .Append(box.MinLon.ToString("F3", c)).Append("..").Append(box.MaxLon.ToString("F3", c))
                .Append(" долготы.");

            return text.ToString();
        }

        private static string Network(DemSource source, WebException error)
        {
            return "Источник \"" + source.Name + "\" не ответил: " + error.Message +
                   " Адрес: " + (string.IsNullOrEmpty(source.BaseUrl) ? OpenTopoRequest.Endpoint : source.BaseUrl) +
                   ". Проверьте подключение и доступность адреса; частично скачанное в кэш не попало.";
        }

        /// <summary>401 и 429 - разные беды с разным лечением: в первом случае ключ неверный, во
        /// втором - рабочий, но квота на сегодня выбрана.</summary>
        private static Exception Translate(DemSource source, GeoBox box, WebException error)
        {
            int status = Status(error);

            if (status == 401 || status == 403)
                return new WebException("OpenTopography не принял ключ (код " + status +
                                        "). Проверьте ключ в настройках модуля: " + OpenTopoRequest.SignUpUrl, error);

            if (status == 429)
                return new WebException("Исчерпана суточная квота вашего ключа OpenTopography (код 429). " +
                                        "Квота личная и восстанавливается на следующие сутки.", error);

            return new WebException("Источник \"" + source.Name + "\" не ответил: " + error.Message +
                                    " Адрес: " + OpenTopoRequest.Describe(source.OpenTopoType, box, null), error);
        }

        private static int Status(WebException error)
        {
            if (error.Data.Contains("HttpStatusCode"))
            {
                object code = error.Data["HttpStatusCode"];
                if (code is int) return (int)code;
            }

            foreach (int candidate in new[] { 401, 403, 429 })
                if (error.Message.IndexOf("(" + candidate.ToString(CultureInfo.InvariantCulture) + ")",
                                          StringComparison.Ordinal) >= 0) return candidate;

            return 0;
        }

        private static string OpenTopoPath(string cacheRoot, string demType, GeoBox box)
        {
            return Path.Combine(Path.Combine(cacheRoot, "opentopo"), OpenTopoFileName(demType, box) + ".tif");
        }

        private static string OpenTopoFileName(string demType, GeoBox box)
        {
            var c = CultureInfo.InvariantCulture;
            string name = demType + "_" +
                          box.MinLon.ToString("F4", c) + "_" + box.MinLat.ToString("F4", c) + "_" +
                          box.MaxLon.ToString("F4", c) + "_" + box.MaxLat.ToString("F4", c);

            var safe = new StringBuilder(name.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char symbol in name)
                safe.Append(Array.IndexOf(invalid, symbol) >= 0 ? '_' : symbol);

            return safe.ToString();
        }

        private static void Report(Action<int, int, string> progress, int done, int total, string stage)
        {
            if (progress != null) progress(done, total, stage);
        }

        private static BuildResult Fail(string error)
        {
            return new BuildResult { Ok = false, Error = error };
        }
    }
}
