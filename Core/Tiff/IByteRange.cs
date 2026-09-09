namespace DemLoader.Core.Tiff
{
    /// <summary>Случайный доступ к байтам файла. За интерфейсом либо HTTP Range к бакету,
    /// либо локальный файл: COG читается кусками, целиком его никогда не грузим.</summary>
    internal interface IByteRange
    {
        byte[] Read(long offset, int length);
    }
}
