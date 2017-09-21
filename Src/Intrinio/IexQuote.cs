using System;

namespace Intrinio
{
    public class IexQuote : IQuote
    {
        public String Type { get; }
        public String Ticker { get; }
        public Decimal Price { get; }
        public long Size { get; }
        public Decimal Timestamp { get; }

        public IexQuote(String Type, String Ticker, Decimal Price, long Size, Decimal Timestamp)
        {
            this.Type = Type;
            this.Ticker = Ticker;
            this.Price = Price;
            this.Size = Size;
            this.Timestamp = Timestamp;
        }

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
