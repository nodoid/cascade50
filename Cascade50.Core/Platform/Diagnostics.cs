using System;

namespace Cascade50.Core.Platform;

/// <summary>
/// Errors the app recovers from (a game or icon that throws is caught so the app keeps running).
/// They are reported here so the soak test can list them.
/// </summary>
public static class Diagnostics
{
    public static event Action<Exception, string> Reported;

    public static void Report(Exception e, string context) => Reported?.Invoke(e, context);
}
