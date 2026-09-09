using DemLoader.Core.Dem;

namespace DemLoader.Core.Job
{
    /// <summary>Глобальные настройки модуля - к уже построенным поверхностям отношения не имеют
    /// (те живут в задании внутри чертежа, см. DemJob). Живут в
    /// %AppData%\Abr\DemLoader\settings.json, схема повторяет AbrBasemap
    /// (distmaps/Core/Job/BasemapSettings.cs).
    ///
    /// Значения по умолчанию объявлены здесь, а не в DemLoaderService: Core не знает про Autodesk
    /// и виден тестовому проекту, а Cad - нет. DemLoaderService.DefaultMaxPoints/DefaultTimeoutMs
    /// ссылаются сюда, чтобы число жило в одном месте.</summary>
    internal sealed class DemSettings
    {
        public const int DefaultMaxPoints = 250000;
        public const int DefaultTimeoutMs = 30000;

        /// <summary>Личный ключ OpenTopography. Пустая строка, а не null - ключа нет.
        /// Условия сервиса запрещают вшивать ключ в приложение и раздавать его третьим лицам,
        /// поэтому во встроенном каталоге источников ключа нет и быть не может
        /// (см. OpenTopoRequest и SourceCatalog).</summary>
        public string OpenTopoApiKey = string.Empty;

        /// <summary>Потолок числа точек сетки. Выше него GridPlan поднимает шаг и говорит об
        /// этом в оценке - молча усечь область нельзя.</summary>
        public int MaxPoints = DefaultMaxPoints;

        /// <summary>Папка кэша скачанных кусков тайлов.</summary>
        public string CacheRoot = DemCache.DefaultRoot;

        /// <summary>Таймаут сетевого запроса, мс.</summary>
        public int TimeoutMs = DefaultTimeoutMs;
    }
}
