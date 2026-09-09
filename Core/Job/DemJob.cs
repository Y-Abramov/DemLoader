using System;
using System.Collections.Generic;
using System.Globalization;

namespace DemLoader.Core.Job
{
    /// <summary>Параметры построенной поверхности. Пишутся в чертёж и читаются DEMUPDATE:
    /// повтор построения не спрашивает пользователя ни о чём.
    ///
    /// Все поля - простые типы. Ни ObjectId, ни DateTime, ни перечислений: задание уезжает
    /// через JSON, который на net48 собирает JavaScriptSerializer, а на net8 - System.Text.Json,
    /// и эти два сериализатора расходятся ровно на сложных типах (см. DemJobJson). Контур
    /// хранится двумя плоскими массивами координат по той же причине - вложенные структуры
    /// точек ни один из двух сериализаторов не читает одинаково.</summary>
    internal sealed class DemJob
    {
        public const string KindWindow = "Window";
        public const string KindPolyline = "Polyline";
        public const string KindCorridor = "Corridor";

        public string SourceId;
        public string CsCode;

        /// <summary>Идентификатор системы координат Robur (Guid из реестра ProjEngine),
        /// в которой строилась поверхность. Хранится, чтобы dem_update пересчитал
        /// в той же СК. Civil-версия этого поля не имела - там СК читалась из чертежа;
        /// у старых заданий пустая строка.</summary>
        public string CoordSystemId;

        public double MinX;
        public double MinY;
        public double MaxX;
        public double MaxY;

        public double Step;
        public double ZShift;

        public string SurfaceName;

        /// <summary>Дескриптор построенной поверхности (Handle), строкой. По нему DEMUPDATE и
        /// DEMERASE находят, что перестроить или удалить, - имя для этого ненадёжно:
        /// поверхность переименовывают в области инструментов, и тогда задание указывало бы
        /// в никуда.
        /// Строкой, а не типом Handle: Core не знает про Autodesk, разбор делает чертёжный слой
        /// (тот же приём, что BasemapJob.ImageHandle в AbrBasemap).
        ///
        /// Пусто у заданий, записанных до появления поля, - тогда поверхность ищется по имени.</summary>
        public string SurfaceHandle;

        /// <summary>Способ выбора области: KindWindow / KindPolyline / KindCorridor.</summary>
        public string AreaKind;

        /// <summary>Дескриптор трассы строкой (Handle), пусто вне KindCorridor. Именно Handle,
        /// а не ObjectId: ObjectId живёт только в текущем сеансе открытого чертежа, а задание
        /// читается в следующем - там тот же ObjectId укажет уже в другой объект или в никуда.
        /// Строкой, а не типом Handle: Core не знает про Autodesk, разбор делает чертёжный слой.</summary>
        public string AlignmentId;

        /// <summary>Ширина коридора в метрах, 0 вне KindCorridor.</summary>
        public double CorridorWidth;

        public List<double> ContourPointsX = new List<double>();
        public List<double> ContourPointsY = new List<double>();

        /// <summary>Момент построения, ISO 8601 в UTC. Строкой, а не DateTime - см. DemJobJson.</summary>
        public string CreatedUtc;

        /// <summary>Штамп времени в том виде, в котором его ждёт CreatedUtc. Формат держится
        /// в одном месте: под локалью ru-RU обычный ToString() дал бы дату вида "04.09.2026",
        /// которую обратно уже никто не разберёт.</summary>
        public static string Stamp(DateTime moment)
        {
            return moment.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }
    }
}
