namespace SharpTrader.MarketSimulator
{
    class MarketRequest<T> : IRequest<T>
    {
        public RequestStatus Status { get; internal set; }
        public T Result { get; }
        public string ErrorInfo { get; internal set; }

        public bool IsSuccessful => Status == RequestStatus.Completed;

        public MarketRequest(RequestStatus status, T res)
        {
            Status = status;
            Result = res;
        }

        public static IRequest<T> Completed(T val)
        {
            return new MarketRequest<T>(RequestStatus.Completed, val);
        }

        public static IRequest<T> FromError(string errorInfo)
        {
            return new MarketRequest<T>(RequestStatus.Failed, default(T)) { ErrorInfo = errorInfo };
        }
    }
}
