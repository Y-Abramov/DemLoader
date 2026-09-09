namespace DemLoader.Core.Sources
{
    internal sealed class DemSource
    {
        public string Id;
        public string Name;
        public string BaseUrl;
        public int ResolutionMarker;      // 10 для GLO-30, 30 для GLO-90
        public double StepMetres;         // родное разрешение: мельче него шаг сетки не опускаем
        public bool RequiresKey;
        public string UserAgent;
        public string Copyright;
        public string VerticalDatum;
        public string SurfaceKind;
        public string OpenTopoType;       // demtype для OpenTopography, пусто для Copernicus
    }
}
