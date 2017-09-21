using System;
using System.Threading;

namespace Intrinio
{
    public class QuoteHandler
    {
        public RealTimeClient Client { get; internal set; }

        public delegate void QuoteDelegate(IQuote quote);
        public event QuoteDelegate OnQuote;

        public void Listen()
        {
            try
            {
                while(true)
                {
                    IQuote quote = this.Client.GetNextQuote();
                    this.OnQuote(quote);
                }
            }
            catch (ThreadAbortException e) { }
        }
    }
}