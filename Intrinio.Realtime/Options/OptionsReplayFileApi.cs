namespace Intrinio.Realtime.Options;

using System;
using System.Collections.Generic;

/// <summary>A replay file returned by the anticipated options replay endpoint.</summary>
internal sealed record OptionsReplayFileDescriptor(string Name, string Url, decimal? Size);

/// <summary>The anticipated response shape returned by the options replay endpoint.</summary>
internal sealed record OptionsReplayFilesResult(IReadOnlyList<OptionsReplayFileDescriptor> Files);

/// <summary>
/// Narrow adapter for the anticipated <c>OptionsApi.GetOptionsReplayFiles(string source, DateTime? date)</c>
/// SDK method. Keeping this boundary internal makes descriptors and downloads injectable in tests.
/// </summary>
internal interface IOptionsReplayFileApi
{
    OptionsReplayFilesResult GetOptionsReplayFiles(string source, DateTime? date);
}

/// <summary>Placeholder until Intrinio.SDK publishes the options replay endpoint.</summary>
internal sealed class PendingOptionsReplayFileApi : IOptionsReplayFileApi
{
    public OptionsReplayFilesResult GetOptionsReplayFiles(string source, DateTime? date) =>
        throw new NotSupportedException(
            "Intrinio.SDK does not yet expose OptionsApi.GetOptionsReplayFiles(source, date) " +
            "(anticipated endpoint: GET /options/replay). Use the local-file ReplayClient constructor " +
            "or inject an internal replay-file adapter in tests.");
}
