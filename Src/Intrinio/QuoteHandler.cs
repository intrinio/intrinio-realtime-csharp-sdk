using System;
using System.Threading;

namespace Intrinio
{
    /// <summary>
    /// A handler for incoming price quotes
    /// </summary>
    public class QuoteHandler
    {
        /// <summary>
        /// A reference to the Intrinio RealTimeClient
        /// </summary>
        public RealTimeClient Client { get; internal set; }

        /// <summary>
        /// A delegate for handling quotes
        /// </summary>
        /// <param name="quote">An IQuote</param>
        public delegate void QuoteDelegate(IQuote quote);

        /// <summary>
        /// An event for when a quote is provided for handling
        /// </summary>
        public event QuoteDelegate OnQuote;

        /// <summary>
        /// Loops indefinitely, blocking until a quote can be retrieved from the client and emitted to the OnQuote event
        /// </summary>
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