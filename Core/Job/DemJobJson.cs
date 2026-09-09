using System;
using System.Collections.Generic;
#if NET48
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif

namespace DemLoader.Core.Job
{
    /// <summary>JSON задания. Тот же приём, что в AbrBasemap (distmaps/Core/Job/BasemapJobJson.cs):
    /// дата хранится строкой ISO 8601 в UTC, потому что JavaScriptSerializer (net48) пишет DateTime
    /// как "\/Date(мс)\/", а System.Text.Json (net8) - своей строкой, и ни то, ни другое не читается
    /// другой сборкой. Задание может быть создано на одной версии Civil 3D и прочитано на другой.
    ///
    /// Дробные числа оба сериализатора пишут инвариантно (точка, не запятая) независимо от локали -
    /// это проверяет DemJobTests под ru-RU, чтобы откат на ручное форматирование сразу ловился.</summary>
    internal static class DemJobJson
    {
        private sealed class Dto
        {
            public string sourceId { get; set; }
            public string csCode { get; set; }
            public string coordSystemId { get; set; }
            public double minX { get; set; }
            public double minY { get; set; }
            public double maxX { get; set; }
            public double maxY { get; set; }
            public double step { get; set; }
            public double zShift { get; set; }
            public string surfaceName { get; set; }
            public string surfaceHandle { get; set; }
            public string areaKind { get; set; }
            public string alignmentId { get; set; }
            public double corridorWidth { get; set; }
            public List<double> contourX { get; set; }
            public List<double> contourY { get; set; }
            public string createdUtc { get; set; }
        }

        public static string Save(DemJob job)
        {
            if (job == null) throw new ArgumentNullException("job");

            var dto = new Dto
            {
                sourceId = job.SourceId,
                csCode = job.CsCode,
                coordSystemId = job.CoordSystemId,
                minX = job.MinX, minY = job.MinY, maxX = job.MaxX, maxY = job.MaxY,
                step = job.Step,
                zShift = job.ZShift,
                surfaceName = job.SurfaceName,
                surfaceHandle = job.SurfaceHandle,
                areaKind = job.AreaKind,
                alignmentId = job.AlignmentId,
                corridorWidth = job.CorridorWidth,
                contourX = job.ContourPointsX ?? new List<double>(),
                contourY = job.ContourPointsY ?? new List<double>(),
                createdUtc = job.CreatedUtc
            };

#if NET48
            return new JavaScriptSerializer().Serialize(dto);
#else
            return JsonSerializer.Serialize(dto);
#endif
        }

        public static DemJob Load(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
#if NET48
                var dto = new JavaScriptSerializer().Deserialize<Dto>(json);
#else
                var dto = JsonSerializer.Deserialize<Dto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
#endif
                if (dto == null || string.IsNullOrEmpty(dto.sourceId)) return null;
                if (dto.minX >= dto.maxX || dto.minY >= dto.maxY) return null;
                if (dto.step <= 0.0) return null;

                var x = dto.contourX ?? new List<double>();
                var y = dto.contourY ?? new List<double>();

                // Разъехавшиеся по длине массивы - не «половина контура», а мусор: точки в них
                // соответствуют друг другу по индексу, и восстановить, где потерялась координата,
                // уже нельзя. Молча строить по обрезанной паре значило бы построить не тот участок.
                if (x.Count != y.Count) return null;

                return new DemJob
                {
                    SourceId = dto.sourceId,
                    CsCode = dto.csCode,
                    CoordSystemId = dto.coordSystemId ?? string.Empty,
                    MinX = dto.minX, MinY = dto.minY, MaxX = dto.maxX, MaxY = dto.maxY,
                    Step = dto.step,
                    ZShift = dto.zShift,
                    SurfaceName = dto.surfaceName,
                    SurfaceHandle = dto.surfaceHandle,
                    AreaKind = dto.areaKind,
                    AlignmentId = dto.alignmentId,
                    CorridorWidth = dto.corridorWidth,
                    ContourPointsX = x,
                    ContourPointsY = y,
                    CreatedUtc = dto.createdUtc
                };
            }
            catch (Exception)
            {
                return null;   // чертёж мог быть правлен руками - это не повод падать
            }
        }
    }
}
