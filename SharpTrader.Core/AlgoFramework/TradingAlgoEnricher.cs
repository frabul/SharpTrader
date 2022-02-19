using Serilog.Core;
using Serilog.Events;
using System;
using System.Runtime.CompilerServices;

namespace SharpTrader.AlgoFramework
{
    /// <summary>
    ///  Enriches events with MarketTime property
    /// </summary>
    public class MarketTimeEnricher : Serilog.Core.ILogEventEnricher
    {
        private IMarketApi MarketApi;
        /// <summary>
        /// The property name added to enriched log events.
        /// </summary>
        public const string MarketTimeName = "MarketTime";

        /// <summary>
        /// Enrich the log event.
        /// </summary>
        /// <param name="logEvent">The log event to enrich.</param> 
        /// <param name="propertyFactory">Factory for creating new properties to add to the event.</param>
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var propToAdd = propertyFactory.CreateProperty(MarketTimeName, MarketApi.Time);
            logEvent.AddPropertyIfAbsent(propToAdd);
        }
        public MarketTimeEnricher(IMarketApi market)
        {
            MarketApi = market;
        }
    }

}
