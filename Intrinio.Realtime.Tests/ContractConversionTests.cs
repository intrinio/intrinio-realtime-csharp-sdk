using System;
using System.Diagnostics;
using System.Globalization;
using Intrinio.Realtime.Composite;
using Intrinio.Realtime.Equities;
using Intrinio.Realtime.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Intrinio.Realtime.Tests;

[TestClass]
public class ContractConversionTests
{
    [TestInitialize]
    public void Setup()
    {
        
    }
    
    [TestMethod]
    public void AcceptsNonStandard()
    {
        string output = Options.Config.ConvertNonstandardContractToStandardContract("AAPL250130P00010000");
        Assert.AreEqual("AAPL__250130P00010000", output);
        
        output = Options.Config.ConvertNonstandardContractToStandardContract("A250130P00010000");
        Assert.AreEqual("A_____250130P00010000", output);
    }
    
    [TestMethod]
    public void AcceptsNonStandardWithTrailingNumber()
    {
        string output = Options.Config.ConvertNonstandardContractToStandardContract("AAPL2250130P00010000");
        Assert.AreEqual("AAPL2_250130P00010000", output);
    }
}