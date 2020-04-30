namespace SharpTrader
{
    public interface IRequest
    {
        RequestStatus Status { get; }
        string ErrorInfo { get; }
        bool IsSuccessful { get; }
    }
}
