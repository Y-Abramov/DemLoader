using System;
using System.IO;
using System.Text;
using DemLoader.Core.Dem;
#if NET48
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif

namespace DemLoader.Core.Job
{
    /// <summary>Чтение и запись настроек модуля. Схема - как у AbrBasemap
    /// (distmaps/Core/Job/BasemapSettingsStore.cs): на net48 JavaScriptSerializer, на net8
    /// System.Text.Json с IncludeFields (настройки - поля, не свойства).
    ///
    /// Отсутствующий и битый файл дают настройки по умолчанию, а не исключение: это
    /// НЕОБЯЗАТЕЛЬНЫЕ пользовательские настройки, и модуль обязан работать без них. Правило то
    /// же, что у SourceCatalog.Load - тихий запасной путь только там, где данные необязательны;
    /// обязательные данные (задание в чертеже, тайл источника) отказывают громко.</summary>
    internal sealed class DemSettingsStore
    {
        private readonly string _path;

        public DemSettingsStore(string path) { _path = path; }

        public static string DefaultPath
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(Path.Combine(Path.Combine(root, "Abr"), "DemLoader"), "settings.json");
            }
        }

        public DemSettings Load()
        {
            if (!File.Exists(_path)) return new DemSettings();

            DemSettings settings;
            try
            {
                string json = File.ReadAllText(_path, Encoding.UTF8);
#if NET48
                settings = new JavaScriptSerializer().Deserialize<DemSettings>(json);
#else
                settings = JsonSerializer.Deserialize<DemSettings>(
                    json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true });
#endif
            }
            catch (Exception)
            {
                // Файл правят руками - разбитый JSON не повод не дать пользователю работать.
                return new DemSettings();
            }

            return Normalize(settings);
        }

        public void Save(DemSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");

            string directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

#if NET48
            string json = new JavaScriptSerializer().Serialize(settings);
#else
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
#endif
            File.WriteAllText(_path, json, new UTF8Encoding(false));
        }

        /// <summary>Пустые и бессмысленные значения заменяются умолчаниями. Файл могли править
        /// руками, а пустая папка кэша или нулевой таймаут уронили бы уже саму загрузку -
        /// далеко от места, где ошибку допустили.</summary>
        private static DemSettings Normalize(DemSettings settings)
        {
            if (settings == null) return new DemSettings();

            if (settings.OpenTopoApiKey == null) settings.OpenTopoApiKey = string.Empty;
            else settings.OpenTopoApiKey = settings.OpenTopoApiKey.Trim();

            if (settings.MaxPoints <= 0) settings.MaxPoints = DemSettings.DefaultMaxPoints;
            if (settings.TimeoutMs <= 0) settings.TimeoutMs = DemSettings.DefaultTimeoutMs;
            if (string.IsNullOrEmpty(settings.CacheRoot)) settings.CacheRoot = DemCache.DefaultRoot;

            return settings;
        }
    }
}
