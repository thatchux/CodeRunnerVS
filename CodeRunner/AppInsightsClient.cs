using System;
using Microsoft.ApplicationInsights;

namespace CodeRunner
{
  static class AppInsightsClient
  {
    static readonly TelemetryClient telemetry;

    static AppInsightsClient()
    {
      // Read instrumentation key from environment to allow opt-out or custom keys.
      // If the env var is missing or empty, telemetry is disabled (telemetry == null).
      try
      {
        var key = Environment.GetEnvironmentVariable("CODERUNNER_APPINSIGHTS_KEY");
        if (!string.IsNullOrWhiteSpace(key))
        {
          telemetry = new TelemetryClient();
          telemetry.InstrumentationKey = key;
        }
        else
        {
          telemetry = null;
        }
      }
      catch
      {
        telemetry = null;
      }
    }

    public static void trackEvent(string eventName)
    {
      try
      {
        telemetry?.TrackEvent(eventName);
      }
      catch
      {
        // swallow telemetry exceptions to avoid affecting the host.
      }
    }
  }
}
