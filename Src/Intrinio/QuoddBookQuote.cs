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
        public BigDecimal AskSize { get; }
        
        /// <summary>
        /// Time of the quote in milliseconds
        /// </summary>
        public BigDecimal QuoteTime { get; }
        
        /// <summary>
        /// Record Transaction Level - number of records published that day
        /// </summary>
        public BigDecimal Rtl { get; }
        
        /// <summary>
        /// Stock Symbol for the security
        /// </summary>
        public String Ticker { get; }
        
        /// <summary>
        /// The market center from which the ask is being quoted
        /// </summary>
        public String AskExchange { get; }
        
        /// <summary>
        /// The price a seller is willing to accept for a security
        /// </summary>
        public BigDecimal AskPrice4d { get; }
        
        /// <summary>
        /// The market center from which the bid is being quoted
        /// </summary>
        public String BidExchange { get; }
        
        /// <summary>
        /// A bid price is the price a buyer is willing to pay for a security.
        /// </summary>
        public BigDecimal BidPrice4d { get; }
        
        /// <summary>
        /// The bid size number of shares being offered for purchase at a specified bid price
        /// </summary>
        public BigDecimal BidSize { get; }
        
        /// <summary>
        /// Internal Quodd ID defining Source of Data
        /// </summary>
        public Integer ProtocolId { get; }
        
        /// <summary>
        /// Underlying symbol for a particular contract
        /// </summary>
        public String RootTicker { get; }
        
        /// <summary>
        /// Initializes an QuoddBookQuote
        /// </summary>
        public QuoddBookQuote(BigDecimal AskSize, BigDecimal QuoteTime, BigDecimal Rtl, String Ticker, String AskExchange, BigDecimal AskPrice4d, String BidExchange, BigDecimal BidPrice4d, BigDecimal BidSize, Integer ProtocolId, String RootTicker)
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
                result.Append("RootTicker: ").Append(this.RootTicker);
            }
            if (this.QuoteTime != null)
            {
                result.Append("QuoteTime: ").Append(this.QuoteTime);
            }
            if (this.AskPrice4d != null)
            {
                result.Append("AskPrice4d: ").Append(this.AskPrice4d);
            }
            if (this.AskSize != null)
            {
                result.Append("AskSize: ").Append(this.AskSize);
            }
            if (this.AskExchange != null)
            {
                result.Append("AskExchange: ").Append(this.AskExchange);
            }
            if (this.BidPrice4d != null)
            {
                result.Append("BidPrice4d: ").Append(this.BidPrice4d);
            }
            if (this.BidSize != null)
            {
                result.Append("BidSize: ").Append(this.BidSize);
            }
            if (this.ProtocolId != null)
            {
                result.Append("ProtocolId: ").Append(this.ProtocolId);
            }
            if (this.BidExchange != null)
            {
                result.Append("BidExchange: ").Append(this.BidExchange);
            }
            if (this.Rtl != null)
            {
                result.Append("Rtl: ").Append(this.Rtl);
            }
            
            result.Append(")");

            return result.ToString();
        }
    }
}
