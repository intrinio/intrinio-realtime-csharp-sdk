namespace Intrinio.Realtime.Composite;

public static class DataCacheFactory
{
    public static IDataCache Create()
    {
        return new DataCache();
    }
}