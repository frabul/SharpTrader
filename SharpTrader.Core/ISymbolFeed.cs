using System;
using System.Threading.Tasks;

namespace SharpTrader
{
    public interface ISymbolFeed
    {
        event Action<ISymbolFeed, IBaseData> OnData;
        ISymbolInfo Symbol { get; }
        string MarketName { get; }
        double Spread { get; }
        double Bid { get; }
        double Ask { get; }
        DateTime Time { get; }

        /// <summary>
        /// Returns market data history ( candlesticks ) from give time
        /// </summary>  
        Task<TimeSerie<ITradeBar>> GetHistoryNavigator(DateTime historyStartTime);

        /// <summary>
        /// Helper method to fix the order amount ad price in order to be compliant to the symbol limits
        /// </summary> 
        /// <returns>(decimal amount, decimal price) </returns>
        (decimal price, decimal amount) GetOrderAmountAndPriceRoundedDown(decimal oderAmout, decimal exitPrice);
        /// <summary>
        /// Helper method to fix the order amount ad price in order to be compliant to the symbol limits
        /// </summary> 
        /// <returns>(decimal amount, decimal price) </returns>
        (decimal price, decimal amount) GetOrderAmountAndPriceRoundedUp(decimal oderAmout, decimal exitPrice);
    }
}
