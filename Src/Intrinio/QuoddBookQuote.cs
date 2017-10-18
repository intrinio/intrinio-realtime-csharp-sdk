using Newtonsoft.Json;
using System;
using System.Text;

namespace Intrinio
{
    /// <summary>
    /// A Book Quote from QUODD
    /// </summary>
    public class QuoddBookQuote : IQuote
    {
        /// <summary>
        /// The amount of a security that a market maker is offering to sell at the ask price
        /// </summary>
        [JsonProperty("ask_size")]
        public Int64? AskSize { get; }

        /// <summary>
        /// Time of the quote in milliseconds
        /// </summary>
        [JsonProperty("quote_time")]
        public Int64? QuoteTime { get; }

        /// <summary>
        /// Record Transaction Level - number of records published that day
        /// </summary>
        [JsonProperty("rtl")]
        public Int64? Rtl { get; }

        /// <summary>
        /// Stock Symbol for the security
        /// </summary>
        [JsonProperty("ticker")]
        public String Ticker { get; }

        /// <summary>
        /// The market center from which the ask is being quoted
        /// </summary>
        [JsonProperty("ask_exchange")]
        public String AskExchange { get; }

        /// <summary>
        /// The price a seller is willing to accept for a security
        /// </summary>
        [JsonProperty("ask_price_4d")]
        public Int64? AskPrice4d { get; }

        /// <summary>
        /// The market center from which the bid is being quoted
        /// </summary>
        [JsonProperty("bid_exchange")]
        public String BidExchange { get; }

        /// <summary>
        /// A bid price is the price a buyer is willing to pay for a security.
        /// </summary>
        [JsonProperty("bid_price_4d")]
        public Int64? BidPrice4d { get; }

        /// <summary>
        /// The bid size number of shares being offered for purchase at a specified bid price
        /// </summary>
        [JsonProperty("bid_size")]
        public Int64? BidSize { get; }

        /// <summary>
        /// Internal Quodd ID defining Source of Data
        /// </summary>
        [JsonProperty("protocol_id")]
        public Int64? ProtocolId { get; }

        /// <summary>
        /// Underlying symbol for a particular contract
        /// </summary>
        [JsonProperty("root_ticker")]
        public String RootTicker { get; }

        /// <summary>
        /// Initializes an QuoddBookQuote
        /// </summary>
        public QuoddBookQuote(Int64? AskSize, Int64? QuoteTime, Int64? Rtl, String Ticker, String AskExchange, Int64? AskPrice4d, String BidExchange, Int64? BidPrice4d, Int64? BidSize, Int64? ProtocolId, String RootTicker)
        {
            this.AskSize = AskSize;
            this.QuoteTime = QuoteTime;
            this.Rtl = Rtl;
            this.Ticker = Ticker;
            this.AskExchange = AskExchange;
            this.AskPrice4d = AskPrice4d;
            this.BidExchange = BidExchange;
            this.BidPrice4d = BidPrice4d;
            this.BidSize = BidSize;
            this.ProtocolId = ProtocolId;
            this.RootTicker = RootTicker;
        }

        /// <summary>
        /// Returns a string representation of the quote
        /// </summary>
        /// <returns>A string representation of the quote</returns>
        public override string ToString()
        {
            StringBuilder result = new StringBuilder();

            result.Append("Intrinio.QuoddBookQuote(");

            if (this.Ticker != null)
            {
                result.Append("Ticker: ").Append(this.Ticker);
            }
            if (this.RootTicker != null)
            {
                result.Append(", RootTicker: ").Append(this.RootTicker);
            }
            if (this.QuoteTime != null)
            {
                result.Append(", QuoteTime: ").Append(this.QuoteTime);
            }
            if (this.AskPrice4d != null)
            {
                result.Append(", AskPrice4d: ").Append(this.AskPrice4d);
            }
            if (this.AskSize != null)
            {
                result.Append(", AskSize: ").Append(this.AskSize);
            }
            if (this.AskExchange != null)
            {
                result.Append(", AskExchange: ").Append(this.AskExchange);
            }
            if (this.BidPrice4d != null)
            {
                result.Append(", BidPrice4d: ").Append(this.BidPrice4d);
            }
            if (this.BidSize != null)
            {
                result.Append(", BidSize: ").Append(this.BidSize);
            }
            if (this.ProtocolId != null)
            {
                result.Append(", ProtocolId: ").Append(this.ProtocolId);
            }
            if (this.BidExchange != null)
            {
                result.Append(", BidExchange: ").Append(this.BidExchange);
            }
            if (this.Rtl != null)
            {
                result.Append(", Rtl: ").Append(this.Rtl);
            }

            result.Append(")");

            return result.ToString();
        }
    }
}
