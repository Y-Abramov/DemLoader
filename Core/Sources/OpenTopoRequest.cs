using System.Globalization;
using DemLoader.Core.Geo;

namespace DemLoader.Core.Sources
{
    /// <summary>Запрос к OpenTopography. Ключ всегда приходит от пользователя: их условия
    /// прямо запрещают вшивать ключ в приложение и раздавать его третьим лицам.</summary>
    internal static class OpenTopoRequest
    {
        public const string Endpoint = "https://portal.opentopography.org/API/globaldem";
        public const string SignUpUrl = "https://portal.opentopography.org/requestService?service=api";

        public static string Build(string demType, GeoBox box, string apiKey)
        {
            return Endpoint + "?" + Query(demType, box) + "&API_Key=" + apiKey;
        }

        /// <summary>Тот же запрос для журнала и сообщений об ошибке - без ключа.</summary>
        public static string Describe(string demType, GeoBox box, string apiKey)
        {
            return Endpoint + "?" + Query(demType, box) + "&API_Key=<ключ пользователя>";
        }

        private static string Query(string demType, GeoBox box)
        {
            var c = CultureInfo.InvariantCulture;
            return "demtype=" + demType +
                   "&south=" + box.MinLat.ToString(c) +
                   "&north=" + box.MaxLat.ToString(c) +
                   "&west=" + box.MinLon.ToString(c) +
                   "&east=" + box.MaxLon.ToString(c) +
                   "&outputFormat=GTiff";
        }
    }
}
