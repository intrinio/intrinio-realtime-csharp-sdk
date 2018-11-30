using Newtonsoft.Json;
using System;

namespace Intrinio
{
    /// <summary>
    /// A Level 1 (price update) message from Cryptoquote
    /// </summary>
    public class CryptoLevel1Message : IQuote
    {
        /// <summary>
        /// The UTC timestamp of when the data was last updated
        /// </summary>
        [JsonProperty("last_updated")]
        public String LastUpdated { get; }

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
        /// The bid price of the crypto currency pair
        /// </summary>
        [JsonProperty("bid")]
        public float? Bid { get; }

        /// <summary>
        /// The bid size of the crypto currency pair
        /// </summary>
        [JsonProperty("bid_size")]
        public float? BidSize { get; }

        /// <summary>
        /// The ask price of the crypto currency pair
        /// </summary>
        [JsonProperty("ask")]
        public float? Ask { get; }

        /// <summary>
        /// The ask size of the crypto currency pair
        /// </summary>
        [JsonProperty("ask_size")]
        public float? AskSize { get; }

        /// <summary>
        /// The notional change in price
        /// </summary>
        [JsonProperty("change")]
        public float? Change { get; }

        /// <summary>
        /// The percent change in price
        /// </summary>
        [JsonProperty("change_percent")]
        public float? ChangePercent { get; }

        /// <summary>
        /// The volume of the crypto currency pair on the exchange
        /// </summary>
        [JsonProperty("volume")]
        public float? Volume { get; }

        /// <summary>
        /// The open price of the crypto currency pair on the exchange
        /// </summary>
        [JsonProperty("open")]
        public float? Open { get; }

        /// <summary>
        /// The high price of the crypto currency pair on the exchange
        /// </summary>
        [JsonProperty("high")]
        public float? High { get; }

        /// <summary>
        /// The low price of the crypto currency pair on the exchange
        /// </summary>
        [JsonProperty("low")]
        public float? Low { get; }

        /// <summary>
        /// A UTC timestamp of the last trade time of the crypto currency pair on the exchange
        /// </summary>
        [JsonProperty("last_trade_time")]
        public String LastTradeTime { get; }

        /// <summary>
        /// The last trade side of the crypto currency pair on the exchange, either "buy" or "sell"
        /// </summary>
        [JsonProperty("last_trade_side")]
        public String LastTradeSide { get; }

        /// <summary>
        /// The last trade price of the crypto currency pair on the exchange
        /// </summary>
        [JsonProperty("last_trade_price")]
        public float? LastTradePrice { get; }

        /// <summary>
        /// The last trade size of the crypto currency pair on the exchange
        /// </summary>
        [JsonProperty("last_trade_size")]
        public float? LastTradeSize { get; }

        /// <summary>
        /// The type of quote, either "book_update", "ticker", or "trade"
        /// </summary>
        [JsonProperty("type")]
        public String Type { get; }

        /// <summary>
        /// Initializes a CryptoTicker
        /// </summary>
        /// <param name="LastUpdated">The UTC timestamp of when the ticker data was last updated</param>
        /// <param name="PairCode">The code of the crypto currency pair</param>
        /// <param name="PairName">The name of the crypto currency pair</param>
        /// <param name="ExchangeCode">The code fo the crypto exchange</param>
        /// <param name="ExchangeName">The name of the crypto exchange</param>
        /// <param name="Bid">The bid price of the crypto currency pair</param>
        /// <param name="BidSize">The bid size of the crypto currency pair</param>
        /// <param name="Ask">The ask price of the crypto currency pair</param>
        /// <param name="AskSize">The ask size of the crypto currency pair</param>
        /// <param name="Change">The notional change in price"</param>
        /// <param name="ChangePercent">The percent change in price</param>
        /// <param name="Volume">The volume of the crypto currency pair on the exchange</param>
        /// <param name="Open">The open price of the crypto currency pair on the exchange</param>
        /// <param name="High">The high price of the crypto currency pair on the exchange</param>
        /// <param name="Low">The low price of the crypto currency pair on the exchange</param>
        /// <param name="LastTradeTime">A UTC timestamp of the last trade time of the crypto currency pair on the exchange</param>
        /// <param name="LastTradeSide">The last trade side of the crypto currency pair on the exchange, either "buy" or "sell"</param>
        /// <param name="LastTradePrice">The last trade price of the crypto currency pair on the exchange</param>
        /// <param name="LastTradeSize">The last trade size of the crypto currency pair on the exchange</param>
        /// <param name="Type">The type of quote, either "book_update", "ticker", or "trade"</param>
        public CryptoLevel1Message(String LastUpdated, String PairCode, String PairName, String ExchangeCode, String ExchangeName, float? Bid, float? BidSize, float? Ask, float? AskSize, float? Change, float? ChangePercent, float? Volume, float? Open, float? High, float? Low, String LastTradeTime, String LastTradeSide, float? LastTradePrice, float? LastTradeSize, String Type)
        {
            this.PairCode = PairCode;
            this.PairName = PairName;
            this.ExchangeCode = ExchangeCode;
            this.ExchangeName = ExchangeName;
            this.Bid = Bid;
            this.BidSize = BidSize;
            this.Ask = Ask;
            this.AskSize = AskSize;
            this.Change = Change;
            this.ChangePercent = ChangePercent;
            this.Volume = Volume;
            this.Open = Open;
            this.High = High;
            this.Low = Low;
            this.LastTradeTime = LastTradeTime;
            this.LastTradeSide = LastTradeSide;
            this.LastTradePrice = LastTradePrice;
            this.LastTradeSize = LastTradeSize;
            this.Type = Type;
        }

        /// <summary>
        /// Returns a string representation of the ticker
        /// </summary>
        /// <returns>A string representation of the ticker</returns>
        public override string ToString()
        {
            return "Intrinio.CryptoTicker(Type: " + this.Type +
                ", LastUpdated: " + this.LastUpdated +
                ", PairName: " + this.PairName +
                ", PairCode: " + this.PairCode +
                ", ExchangeName: " + this.ExchangeName +
                ", ExchangeCode: " + this.ExchangeCode +
                ", Bid: " + this.Bid +
                ", BidSize: " + this.BidSize +
                ", Ask: " + this.Ask +
                ", AskSize: " + this.AskSize +
                ", Change: " + this.Change +
                ", ChangePercent: " + this.ChangePercent +
                ", Volume: " + this.Volume +
                ", Open: " + this.Open +
                ", High: " + this.High +
                ", Low: " + this.Low +
                ", LastTradeTime: " + this.LastTradeTime +
                ", LastTradeSide: " + this.LastTradeSide +
                ", LastTradePrice: " + this.LastTradePrice +
                ", LastTradeSize: " + this.LastTradeSize +")";
        }
    }
}
