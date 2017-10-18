using System;
using System.Text;

namespace Intrinio
{
    /// <summary>
    /// A Trade Quote from QUODD
    /// </summary>
    public class QuoddTradeQuote : IQuote
    {
        /// <summary>
        /// The security ticker
        /// </summary>
        public String Ticker { get; }

        /// <summary>
        /// Initializes an IexQuote
        /// </summary>
        /// <param name="Ticker">The security ticker</param>
        public QuoddTradeQuote(String Ticker)
        {
            this.Ticker = Ticker;
        }

        /// <summary>
        /// Returns a string representation of the quote
        /// </summary>
        /// <returns>A string representation of the quote</returns>
        public override string ToString()
        {
            StringBuilder result = new StringBuilder();

            result.Append("Intrinio.QuoddTradeQuote(");
            if (this.Ticker != null)
            {
                result.Append("ticker: ").Append(this.Ticker);
            }
            result.Append(")");

            return result.ToString();
        }
    }
}