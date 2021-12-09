using SharpTrader.Storage;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpTrader.Core.BrokersApi.KuCoin
{
    public class KuCoinTradeBarsRepository : TradeBarsRepository
    {
        NLog.Logger Logger = NLog.LogManager.GetLogger(nameof(KuCoinTradeBarsRepository));

        public KuCoinTradeBarsRepository(string dataDir, ChunkFileVersion cv, ChunkSpan chunkSpan) : base(dataDir, cv, chunkSpan)
        {

        }
    }
}
