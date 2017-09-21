using System;

namespace Intrinio
{
    /// <summary>
    /// A Quote from IEX
    /// </summary>
    public class IexQuote : IQuote
    {
        /// <summary>
        /// The type of quote, either "bid", "ask", or "last"
        /// </summary>
        public String Type { get; }

        /// <summary>
        /// The security ticker
        /// </summary>
        public String Ticker { get; }

        /// <summary>
        /// The quote price
        /// </summary>
        public Decimal Price { get; }

        /// <summary>
        /// The size of the order or trade
        /// </summary>
        public long Size { get; }

        /// <summary>
        /// The unix timestamp of the quote
        /// </summary>
        public Decimal Timestamp { get; }

        /// <summary>
        /// Initializes an IexQuote
        /// </summary>
        /// <param name="Type">The type of quote, either "bid", "ask", or "last"</param>
        /// <param name="Ticker">The security ticker</param>
        /// <param name="Price">The quote price</param>
        /// <param name="Size">The size of the order or trade</param>
        /// <param name="Timestamp">The unix timestamp of the quote</param>
        public IexQuote(String Type, String Ticker, Decimal Price, long Size, Decimal Timestamp)
        {
            this.Type = Type;
            this.Ticker = Ticker;
            this.Price = Price;
            this.Size = Size;
            this.Timestamp = Timestamp;
        }

        /// <summary>
        /// Returns a string representation of the quote
        /// </summary>
        /// <returns>A string representation of the quote</returns>
        public override string ToString()
        {
            return "Intrinio.IexQuote(Type: " + this.Type +
                ", Ticker: " + this.Ticker +
                ", Price: " + this.Price +
                ", Size: " + this.Size +
                ", Timestamp: " + this.Timestamp;
        }
    }
}
