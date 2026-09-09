using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
#if NET48
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif

namespace DemLoader.Core.Sources
{
    /// <summary>Каталог источников рельефа: встроенный ресурс (только Copernicus, без ключа)
    /// плюс необязательный пользовательский файл. OpenTopography в builtin-sources.json
    /// намеренно не входит - условия сервиса запрещают вшивать ключ в приложение, ключ всегда
    /// свой у пользователя (см. OpenTopoRequest). Пользовательский файл перекрывает встроенный
    /// по id целой записью (не полями по отдельности); неизвестные id добавляются.</summary>
    internal static class SourceCatalog
    {
        // Единственный ресурс с таким суффиксом в сборке сейчас - builtin-sources.json.
        // Суффиксом, а не точным логическим именем, потому что тестовый проект линкует Core
        // напрямую и не разделяет с основным RootNamespace, из которого MSBuild строит имя.
        private const string ResourceSuffix = "builtin-sources.json";

        public static string DefaultUserPath
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(Path.Combine(Path.Combine(root, "Abr"), "DemLoader"), "sources.json");
            }
        }

        /// <summary>Встроенный каталог, разобранный в список источников.</summary>
        public static IList<DemSource> BuiltIn()
        {
            return Deserialize(BuiltInRaw());
        }

        /// <summary>Встроенный каталог как есть, без разбора - чтобы тест мог просканировать
        /// текст на предмет чего-то похожего на вшитый ключ, не завися от парсера.</summary>
        public static string BuiltInRaw()
        {
            var assembly = typeof(SourceCatalog).Assembly;
            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (!name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                using (var stream = assembly.GetManifestResourceStream(name))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }

            throw new InvalidOperationException("Встроенный каталог источников не найден в ресурсах сборки.");
        }

        public static IList<DemSource> ReadFile(string path)
        {
            return Deserialize(File.ReadAllText(path, Encoding.UTF8));
        }

        public static void Save(string path, IList<DemSource> sources)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, Serialize(sources), new UTF8Encoding(false));
        }

        /// <summary>Встроенные источники плюс пользовательские (если файл есть). Битый
        /// пользовательский файл не должен обрушивать весь каталог - в этом случае
        /// используются только встроенные источники, как при отсутствии файла.</summary>
        public static IList<DemSource> Load(string userPath)
        {
            var result = new List<DemSource>(BuiltIn());

            if (!string.IsNullOrEmpty(userPath) && File.Exists(userPath))
            {
                IList<DemSource> userSources;
                try { userSources = ReadFile(userPath); }
                catch (Exception)
                {
                    return result;
                }

                foreach (var source in userSources)
                {
                    int existing = -1;
                    for (int i = 0; i < result.Count; i++)
                    {
                        if (result[i].Id == source.Id) { existing = i; break; }
                    }

                    if (existing >= 0) result[existing] = source;
                    else result.Add(source);
                }
            }

            return result;
        }

        private sealed class Dto
        {
            public string id { get; set; }
            public string name { get; set; }
            public string baseUrl { get; set; }
            public int resolutionMarker { get; set; }
            public double stepMetres { get; set; }
            public bool requiresKey { get; set; }
            public string userAgent { get; set; }
            public string copyright { get; set; }
            public string verticalDatum { get; set; }
            public string surfaceKind { get; set; }
            public string openTopoType { get; set; }
        }

        private static IList<DemSource> Deserialize(string json)
        {
#if NET48
            var items = new JavaScriptSerializer().Deserialize<List<Dto>>(json);
#else
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var items = JsonSerializer.Deserialize<List<Dto>>(json, options);
#endif
            var result = new List<DemSource>();
            foreach (var dto in items)
            {
                result.Add(new DemSource
                {
                    Id = dto.id,
                    Name = dto.name,
                    BaseUrl = dto.baseUrl,
                    ResolutionMarker = dto.resolutionMarker,
                    StepMetres = dto.stepMetres,
                    RequiresKey = dto.requiresKey,
                    UserAgent = dto.userAgent,
                    Copyright = dto.copyright,
                    VerticalDatum = dto.verticalDatum,
                    SurfaceKind = dto.surfaceKind,
                    OpenTopoType = dto.openTopoType
                });
            }

            return result;
        }

        private static string Serialize(IList<DemSource> sources)
        {
            var items = new List<Dto>();
            foreach (var source in sources)
            {
                items.Add(new Dto
                {
                    id = source.Id,
                    name = source.Name,
                    baseUrl = source.BaseUrl,
                    resolutionMarker = source.ResolutionMarker,
                    stepMetres = source.StepMetres,
                    requiresKey = source.RequiresKey,
                    userAgent = source.UserAgent,
                    copyright = source.Copyright,
                    verticalDatum = source.VerticalDatum,
                    surfaceKind = source.SurfaceKind,
                    openTopoType = source.OpenTopoType
                });
            }

#if NET48
            return new JavaScriptSerializer().Serialize(items);
#else
            return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
#endif
        }
    }
}
