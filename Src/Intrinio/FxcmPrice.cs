using Newtonsoft.Json;
using System;

namespace Intrinio
{
    /// <summary>
    /// A price update message from FXCM
    /// </summary>
    public class FxcmPrice : IQuote
    {
        /// <summary>
        /// The UTC timestamp of when the data was last updated
        /// </summary>
        [JsonProperty("time")]
        public String Time { get; }

        /// <summary>
        /// The code of the fx currency pair
        /// </summary>
        [JsonProperty("code")]
        public String Code { get; }

        /// <summary>
        /// The bid price of the fx currency pair
        /// </summary>
        [JsonProperty("bid_price")]
        public float? BidPrice { get; }

        /// <summary>
        /// The ask price of the fx currency pair
        /// </summary>
        [JsonProperty("ask_price")]
        public float? AskPrice { get; }


        /// <summary>
        /// Initializes a FxcmPrice
        /// </summary>
        /// <param name="Time">The UTC timestamp of the price update</param>
        /// <param name="Code">The code of the fx currency pair</param>
        /// <param name="BidPrice">The bid price of the fx currency pair</param>
        /// <param name="AskPrice">The ask price of the fx currency pair</param>
        public FxcmPrice(String Time, String Code, float? BidPrice, float? AskPrice)
        {
            this.Time = Time;
            this.Code = Code;
            this.BidPrice = BidPrice;
            this.AskPrice = AskPrice;
        }

        /// <summary>
        /// Returns a string representation of the price update
        /// </summary>
        /// <returns>A string representation of the price update</returns>
        public override string ToString()
        {
            return "Intrinio.FxcmPrice(Time: " + this.Time +
                ", Code: " + this.Code +
                ", BidPrice: " + this.BidPrice +
                ", AskPrice: " + this.AskPrice + ")";
        }
    }
}
