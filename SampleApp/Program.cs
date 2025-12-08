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
		static async Task Main(string[] args)
		{
			//await EquitiesSampleApp.Run(args);
			//await OptionsSampleApp.Run(args);
			//await GreekSampleApp.Run(args);
			//await KitchenSinkSampleApp.Run(args);
			OneSecondCandleCsvBuilderExample.Run(args);
		}
	}
}
