using System.Collections.Generic;
using DemLoader.Core.Tiff;

namespace DemLoader.Core.Dem
{
    internal interface IDemTile
    {
        bool Contains(double lon, double lat);
        bool TryGetElevation(double lon, double lat, out double elevation);
    }

    /// <summary>Один тайл Copernicus как источник отметок. Внутренние блоки распаковываются
    /// лениво и кэшируются: площадка 2x2 км помещается в один блок из двенадцати, и качать
    /// ради неё все 29 МБ незачем.</summary>
    internal sealed class DemTileReader : IDemTile
    {
        private readonly IByteRange _source;
        private readonly TiffHeader _header;
        private readonly GeoTiffModel _model;
        private readonly Dictionary<int, float[]> _blocks = new Dictionary<int, float[]>();

        public DemTileReader(IByteRange source)
        {
            _source = source;
            _header = TiffHeader.Read(source);
            _model = GeoTiffModel.From(_header);
        }

        public GeoTiffModel Model { get { return _model; } }

        public bool Contains(double lon, double lat) { return _model.Contains(lon, lat); }

        public bool TryGetElevation(double lon, double lat, out double elevation)
        {
            elevation = 0;
            if (!_model.Contains(lon, lat)) return false;

            double column, row;
            _model.ToPixel(lon, lat, out column, out row);

            int c0 = (int)System.Math.Floor(column), r0 = (int)System.Math.Floor(row);
            int c1 = System.Math.Min(c0 + 1, _model.Width - 1);
            int r1 = System.Math.Min(r0 + 1, _model.Height - 1);
            if (c0 < 0 || r0 < 0) return false;
            // Отсекает только запад/север (c0/r0 < 0), восток/юг уже прижаты выше через Min(..., Width-1).
            // Для PixelIsArea=true (сдвиг 0.5) у GeoTiffModel.Contains есть допуск в полпикселя на
            // западном/северном краю, которого здесь нет - Contains()==true, но этот метод даст false
            // на самом краю. Сейчас не достижимо: Copernicus всегда RasterPixelIsPoint (сдвиг 0), где
            // эта полоса стягивается в ноль. Если появится источник со схемой «площадка» - подрезать
            // column/row до 0 перед floor, как уже сделано для верхнего края.

            double v00, v10, v01, v11;
            if (!TryPixel(c0, r0, out v00) || !TryPixel(c1, r0, out v10) ||
                !TryPixel(c0, r1, out v01) || !TryPixel(c1, r1, out v11)) return false;

            double fx = column - c0, fy = row - r0;
            elevation = v00 * (1 - fx) * (1 - fy) + v10 * fx * (1 - fy)
                      + v01 * (1 - fx) * fy + v11 * fx * fy;
            return true;
        }

        private bool TryPixel(int column, int row, out double value)
        {
            value = 0;

            int tileWidth = (int)_header.TileWidth, tileLength = (int)_header.TileLength;
            int index = (row / tileLength) * _header.TilesAcross + (column / tileWidth);

            float[] block;
            if (!_blocks.TryGetValue(index, out block))
            {
                uint[] offsets = _header.TileOffsets;
                uint[] counts = _header.TileByteCounts;
                if (index < 0 || index >= offsets.Length) return false;

                byte[] packed = _source.Read(offsets[index], (int)counts[index]);
                block = TiffTileReader.Decode(packed, width: tileWidth, height: tileLength,
                    compression: _header.Compression, predictor: _header.Predictor,
                    bytesPerSample: _header.BitsPerSample / 8, samplesPerPixel: _header.SamplesPerPixel,
                    littleEndian: true);

                _blocks[index] = block;
            }

            float raw = block[(row % tileLength) * tileWidth + (column % tileWidth)];
            if (float.IsNaN(raw)) return false;

            value = raw;
            return true;
        }
    }
}
