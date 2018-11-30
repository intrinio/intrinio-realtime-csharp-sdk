using Newtonsoft.Json;
using System;

namespace Intrinio
{
    /// <summary>
    /// A Level 2 (book update) message from Cryptoquote
    /// </summary>
    public class CryptoLevel2Message : IQuote
    {
        /// <summary>
        /// The code of the crypto currency pair
        /// </summary>
        [JsonProperty("pair_code")]
        public String PairCode { get; }

        /// <summary>
        /// The code of the crypto currency pair
        /// </summary>
        [JsonProperty("pair_name")]
        public String PairName { get; }

        /// <summary>
        /// The code of the crypto exchange
        /// </summary>
        [JsonProperty("exchange_code")]
        public String ExchangeCode { get; }

        /// <summary>
        /// The name of the crypto exchange
        /// </summary>
        [JsonProperty("exchange_name")]
        public String ExchangeName { get; }

        /// <summary>
        /// The price of the crypto currency pair of the book update
        /// </summary>
        [JsonProperty("price")]
        public float Price { get; }

        /// <summary>
        /// The side of the book update, either "buy" or "sell"
        /// </summary>
        [JsonProperty("side")]
        public String Side { get; }

        /// <summary>
        /// The size of the book update
        /// </summary>
        [JsonProperty("size")]
        public float Size { get; }

        /// <summary>
        /// The type of quote, either "book_update", "ticker", or "trade"
        /// </summary>
        [JsonProperty("type")]
        public String Type { get; }

        /// <summary>
        /// Initializes a CryptoBookUpdate
        /// </summary>
        /// <param name="PairCode">The code of the crypto currency pair</param>
        /// <param name="PairName">The name of the crypto currency pair</param>
        /// <param name="ExchangeCode">The code fo the crypto exchange</param>
        /// <param name="ExchangeName">The name of the crypto exchange</param>
        /// <param name="Price">The price of the crypto currency pair of the book update</param>
        /// <param name="Side">The side of the book update, either "buy" or "sell"</param>
        /// <param name="Size">The size of the book update</param>
        /// <param name="Type">The type of quote, either "book_update", "ticker", or "trade"</param>
        public CryptoLevel2Message(String PairCode, String PairName, String ExchangeCode, String ExchangeName, float Price, String Side, float Size, String Type)
        {
            this.PairCode = PairCode;
            this.PairName = PairName;
            this.ExchangeCode = ExchangeCode;
            this.ExchangeName = ExchangeName;
            this.Price = Price;
            this.Size = Size;
            this.Side = Side;
            this.Type = Type;

        }

        /// <summary>
        /// Returns a string representation of the book_update
        /// </summary>
        /// <returns>A string representation of the book_update</returns>
        public override string ToString()
        {
            return "Intrinio.CryptoBookUpdate(Type: " + this.Type +
                ", PairName: " + this.PairName +
                ", PairCode: " + this.PairCode +
                ", ExchangeName: " + this.ExchangeName +
                ", ExchangeCode: " + this.ExchangeCode +
                ", Price: " + this.Price +
                ", Size: " + this.Size +
                ", Side: " + this.Side + ")";
        }
    }
}
