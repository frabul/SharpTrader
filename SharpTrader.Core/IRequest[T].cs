namespace SharpTrader
{
    public interface IRequest<T> : IRequest
    {
        T Result { get; }
    }
}
