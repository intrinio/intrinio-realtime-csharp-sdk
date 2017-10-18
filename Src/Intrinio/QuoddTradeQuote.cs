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
        /// The difference between the closing price of a security on the current trading day and the previous day's closing price.
        /// </summary>
        public BigDecimal ChangePrice4d { get; }
        
        /// <summary>
        /// A security's intra-day high trading price.
        /// </summary>
        public BigDecimal DayHigh4d { get; }
        
        /// <summary>
        /// Time that the security reached a new high
        /// </summary>
        public BigDecimal DayHighTime { get; }
        
        /// <summary>
        /// A security's intra-day low trading price.
        /// </summary>
        public BigDecimal DayLow4d { get; }
        
        /// <summary>
        /// Time that the security reached a new low
        /// </summary>
        public BigDecimal DayLowTime { get; }
        
        /// <summary>
        /// Extended hours change price (pre or post market)
        /// </summary>
        public BigDecimal ExtChangePrice4d { get; }
        
        /// <summary>
        /// Extended hours last price (pre or post market)
        /// </summary>
        public BigDecimal ExtLastPrice4d { get; }
        
        /// <summary>
        /// Extended hours percent change (pre or post market)
        /// </summary>
        public BigDecimal ExtPercentChange4d { get; }
        
        /// <summary>
        /// Extended hours exchange where last trade took place (Pre or post market)
        /// </summary>
        public String ExtTradeExchange { get; }
        
        /// <summary>
        /// Time of the extended hours trade in milliseconds
        /// </summary>
        public BigDecimal ExtTradeTime { get; }
        
        /// <summary>
        /// The amount of shares traded for a single extended hours trade
        /// </summary>
        public BigDecimal ExtTradeVolume { get; }
        
        /// <summary>
        /// Extended hours tick indicator - up or down
        /// </summary>
        public String ExtUpDown { get; }
        
        /// <summary>
        /// A flag indicating that the stock is halted and not currently trading
        /// </summary>
        public Boolean IsHalted { get; }
        
        /// <summary>
        /// A flag indicating the stock is current short sale restricted - meaning you can not short sale the stock when true
        /// </summary>
        public Boolean IsShortRestricted { get; }
        
        /// <summary>
        /// The price at which the security most recently traded
        /// </summary>
        public BigDecimal LastPrice4d { get; }
        
        /// <summary>
        /// The price at which a security first trades upon the opening of an exchange on a given trading day
        /// </summary>
        public BigDecimal OpenPrice4d { get; }
        
        /// <summary>
        /// The time at which the security opened in milliseconds
        /// </summary>
        public BigDecimal OpenTime { get; }
        
        /// <summary>
        /// The number of shares that that were traded on the opening trade
        /// </summary>
        public BigDecimal OpenVolume { get; }
        
        /// <summary>
        /// The percentage at which the security is up or down since the previous day's trading
        /// </summary>
        public BigDecimal PercentChange4d { get; }
        
        /// <summary>
        /// The security's closing price on the preceding day of trading
        /// </summary>
        public BigDecimal PrevClose4d { get; }
        
        /// <summary>
        /// Internal Quodd ID defining Source of Data
        /// </summary>
        public BigDecimal ProtocolId { get; }
        
        /// <summary>
        /// Underlying symbol for a particular contract
        /// </summary>
        public String RootTicker { get; }
        
        /// <summary>
        /// Record Transaction Level - number of records published that day
        /// </summary>
        public BigDecimal Rtl { get; }
        
        /// <summary>
        /// Stock Symbol for the security
        /// </summary>
        public String Ticker { get; }
        
        /// <summary>
        /// The accumulated total amount of shares traded
        /// </summary>
        public BigDecimal TotalVolume { get; }
        
        /// <summary>
        /// The market center where the last trade occurred
        /// </summary>
        public String TradeExchange { get; }
        
        /// <summary>
        /// The time at which the security last traded in milliseconds
        /// </summary>
        public BigDecimal TradeTime { get; }
        
        /// <summary>
        /// The number of shares that that were traded on the last trade
        /// </summary>
        public BigDecimal TradeVolume { get; }
        
        /// <summary>
        /// Tick indicator - up or down - indicating if the last trade was up or down from the previous trade
        /// </summary>
        public String UpDown { get; }
        
        /// <summary>
        /// NASDAQ volume plus the volumes from other market centers to more accurately match composite volume. Used for NASDAQ Basic
        /// </summary>
        public BigDecimal VolumePlus { get; }
        
        /// <summary>
        /// Volume weighted Average Price. VWAP is calculated by adding up the dollars traded for every transaction (price multiplied by number of shares traded) and then dividing by the total shares traded for the day.
        /// </summary>
        public BigDecimal Vwap4d { get; }
        
        /// <summary>
        /// Initializes an QuoddTradeQuote
        /// </summary>
        public QuoddTradeQuote(BigDecimal ChangePrice4d, BigDecimal DayHigh4d, BigDecimal DayHighTime, BigDecimal DayLow4d, BigDecimal DayLowTime, BigDecimal ExtChangePrice4d, BigDecimal ExtLastPrice4d, BigDecimal ExtPercentChange4d, String ExtTradeExchange, BigDecimal ExtTradeTime, BigDecimal ExtTradeVolume, String ExtUpDown, Boolean IsHalted, Boolean IsShortRestricted, BigDecimal LastPrice4d, BigDecimal OpenPrice4d, BigDecimal OpenTime, BigDecimal OpenVolume, BigDecimal PercentChange4d, BigDecimal PrevClose4d, BigDecimal ProtocolId, String RootTicker, BigDecimal Rtl, String Ticker, BigDecimal TotalVolume, String TradeExchange, BigDecimal TradeTime, BigDecimal TradeVolume, String UpDown, BigDecimal VolumePlus, BigDecimal Vwap4d)
        {
            this.ChangePrice4d = ChangePrice4d;
            this.DayHigh4d = DayHigh4d;
            this.DayHighTime = DayHighTime;
            this.DayLow4d = DayLow4d;
            this.DayLowTime = DayLowTime;
            this.ExtChangePrice4d = ExtChangePrice4d;
            this.ExtLastPrice4d = ExtLastPrice4d;
            this.ExtPercentChange4d = ExtPercentChange4d;
            this.ExtTradeExchange = ExtTradeExchange;
            this.ExtTradeTime = ExtTradeTime;
            this.ExtTradeVolume = ExtTradeVolume;
            this.ExtUpDown = ExtUpDown;
            this.IsHalted = IsHalted;
            this.IsShortRestricted = IsShortRestricted;
            this.LastPrice4d = LastPrice4d;
            this.OpenPrice4d = OpenPrice4d;
            this.OpenTime = OpenTime;
            this.OpenVolume = OpenVolume;
            this.PercentChange4d = PercentChange4d;
            this.PrevClose4d = PrevClose4d;
            this.ProtocolId = ProtocolId;
            this.RootTicker = RootTicker;
            this.Rtl = Rtl;
            this.Ticker = Ticker;
            this.TotalVolume = TotalVolume;
            this.TradeExchange = TradeExchange;
            this.TradeTime = TradeTime;
            this.TradeVolume = TradeVolume;
            this.UpDown = UpDown;
            this.VolumePlus = VolumePlus;
            this.Vwap4d = Vwap4d;
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
                result.Append("Ticker: ").Append(this.Ticker);
            }
            if (this.ChangePrice4d != null)
            {
                result.Append("ChangePrice4d: ").Append(this.ChangePrice4d);
            }
            if (this.DayHigh4d != null)
            {
                result.Append("DayHigh4d: ").Append(this.DayHigh4d);
            }
            if (this.DayHighTime != null)
            {
                result.Append("DayHighTime: ").Append(this.DayHighTime);
            }
            if (this.DayLow4d != null)
            {
                result.Append("DayLow4d: ").Append(this.DayLow4d);
            }
            if (this.DayLowTime != null)
            {
                result.Append("DayLowTime: ").Append(this.DayLowTime);
            }
            if (this.ExtChangePrice4d != null)
            {
                result.Append("ExtChangePrice4d: ").Append(this.ExtChangePrice4d);
            }
            if (this.ExtLastPrice4d != null)
            {
                result.Append("ExtLastPrice4d: ").Append(this.ExtLastPrice4d);
            }
            if (this.ExtPercentChange4d != null)
            {
                result.Append("ExtPercentChange4d: ").Append(this.ExtPercentChange4d);
            }
            if (this.ExtTradeExchange != null)
            {
                result.Append("ExtTradeExchange: ").Append(this.ExtTradeExchange);
            }
            if (this.ExtTradeTime != null)
            {
                result.Append("ExtTradeTime: ").Append(this.ExtTradeTime);
            }
            if (this.ExtTradeVolume != null)
            {
                result.Append("ExtTradeVolume: ").Append(this.ExtTradeVolume);
            }
            if (this.ExtUpDown != null)
            {
                result.Append("ExtUpDown: ").Append(this.ExtUpDown);
            }
            if (this.IsHalted != null)
            {
                result.Append("IsHalted: ").Append(this.IsHalted);
            }
            if (this.IsShortRestricted != null)
            {
                result.Append("IsShortRestricted: ").Append(this.IsShortRestricted);
            }
            if (this.LastPrice4d != null)
            {
                result.Append("LastPrice4d: ").Append(this.LastPrice4d);
            }
            if (this.OpenPrice4d != null)
            {
                result.Append("OpenPrice4d: ").Append(this.OpenPrice4d);
            }
            if (this.OpenTime != null)
            {
                result.Append("OpenTime: ").Append(this.OpenTime);
            }
            if (this.OpenVolume != null)
            {
                result.Append("OpenVolume: ").Append(this.OpenVolume);
            }
            if (this.PercentChange4d != null)
            {
                result.Append("PercentChange4d: ").Append(this.PercentChange4d);
            }
            if (this.PrevClose4d != null)
            {
                result.Append("PrevClose4d: ").Append(this.PrevClose4d);
            }
            if (this.ProtocolId != null)
            {
                result.Append("ProtocolId: ").Append(this.ProtocolId);
            }
            if (this.RootTicker != null)
            {
                result.Append("RootTicker: ").Append(this.RootTicker);
            }
            if (this.Rtl != null)
            {
                result.Append("Rtl: ").Append(this.Rtl);
            }
            if (this.TotalVolume != null)
            {
                result.Append("TotalVolume: ").Append(this.TotalVolume);
            }
            if (this.TradeExchange != null)
            {
                result.Append("TradeExchange: ").Append(this.TradeExchange);
            }
            if (this.TradeTime != null)
            {
                result.Append("TradeTime: ").Append(this.TradeTime);
            }
            if (this.TradeVolume != null)
            {
                result.Append("TradeVolume: ").Append(this.TradeVolume);
            }
            if (this.UpDown != null)
            {
                result.Append("UpDown: ").Append(this.UpDown);
            }
            if (this.VolumePlus != null)
            {
                result.Append("VolumePlus: ").Append(this.VolumePlus);
            }
            if (this.Vwap4d != null)
            {
                result.Append("Vwap4d: ").Append(this.Vwap4d);
            }
            
            result.Append(")");

            return result.ToString();
        }
    }
}
