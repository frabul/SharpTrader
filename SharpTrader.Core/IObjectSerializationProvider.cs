using LiteDB;

namespace SharpTrader
{
    /// <summary>
    /// Classes that implement this interfaces should provide the serialization of their managed obect types
    /// </summary>
    public interface IObjectSerializationProvider
    {
        void RegisterCustomSerializers(BsonMapper mapper);
    }
}
