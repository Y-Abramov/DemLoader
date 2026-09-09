using System;
using System.Globalization;

namespace DemLoader.Core.Dem
{
    /// <summary>Имя объекта в бакете Copernicus. Правило именования - юго-западный угол ячейки
    /// в целых градусах: широта двумя цифрами, долгота тремя, полушарие буквой.</summary>
    internal static class DemTileKey
    {
        public static string Name(double lat, double lon, int resolutionMarker)
        {
            int latCell = (int)Math.Floor(lat);
            int lonCell = (int)Math.Floor(lon);
            return NameOfCell(latCell, lonCell, resolutionMarker);
        }

        public static string NameOfCell(int latCell, int lonCell, int resolutionMarker)
        {
            string ns = latCell < 0 ? "S" : "N";
            string ew = lonCell < 0 ? "W" : "E";

            return string.Format(CultureInfo.InvariantCulture,
                "Copernicus_DSM_COG_{0}_{1}{2:00}_00_{3}{4:000}_00_DEM",
                resolutionMarker, ns, Math.Abs(latCell), ew, Math.Abs(lonCell));
        }

        public static string Url(string baseUrl, double lat, double lon, int resolutionMarker)
        {
            return UrlOfCell(baseUrl, (int)Math.Floor(lat), (int)Math.Floor(lon), resolutionMarker);
        }

        public static string UrlOfCell(string baseUrl, int latCell, int lonCell, int resolutionMarker)
        {
            string name = NameOfCell(latCell, lonCell, resolutionMarker);
            return baseUrl.TrimEnd('/') + "/" + name + "/" + name + ".tif";
        }
    }
}
