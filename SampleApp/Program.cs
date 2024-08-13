using System;
using System.Threading;
using System.Collections.Concurrent;
using Intrinio.Realtime.Equities;
using Serilog;
using Serilog.Core;

namespace SampleApp
{
	class Program
	{
		static void Main(string[] _)
		{
			EquitiesSampleApp.Run(new string[]{});
		}		
	}
}
