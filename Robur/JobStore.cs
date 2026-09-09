using System;
using System.Collections.Generic;
using Topomatic.ApplicationPlatform.Core;
using DemLoader.Core.Job;

namespace DemLoader.Robur
{
    /// <summary>Задание загрузки, привязанное к модели ЦММ.
    ///
    /// Механизм - пользовательские атрибуты модели проекта. Пригодность подтверждена
    /// спайком Task 2 (docs/superpowers/spikes/2026-09-05-robur-terrain-model-write.md):
    /// атрибуты переживают закрытие проекта и перезапуск Robur, фолбэк на файл не нужен.
    ///
    /// ЖИВЬЁМ НЕ ПРОВЕРЯЛСЯ за пределами спайка до гейта Task 13.</summary>
    internal static class JobStore
    {
        private const string JobKey = "abr_dem_job";
        private const string CsKey  = "abr_dem_cs";

        public static void Save(IProjectModel model, DemJob job)
        {
            if (model == null) throw new ArgumentNullException("model");
            if (job == null) throw new ArgumentNullException("job");

            var map = new Dictionary<string, object>
            {
                { JobKey, DemJobJson.Save(job) },
                { CsKey,  job.CoordSystemId ?? string.Empty }
            };
            model.Project.SetUserModelAttributes(model.Uri, model.ModelType, Attributes.Create(map));
        }

        /// <summary>Задание модели либо null, если модель создана не нами.</summary>
        public static DemJob Load(IProjectModel model)
        {
            if (model == null) return null;
            Attributes attrs = model.Project.GetUserModelAttributes(model.Uri, model.ModelType);
            if (attrs == null || !attrs.ContainsKey(JobKey)) return null;

            string raw = attrs.AsString(JobKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return null;
            return DemJobJson.Load(raw);
        }

        public static bool IsOurs(IProjectModel model)
        {
            return Load(model) != null;
        }
    }
}
