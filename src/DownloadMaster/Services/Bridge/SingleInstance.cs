using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DownloadMaster.Services.Bridge;

/// <summary>
/// One running DownloadMaster per user: a second launch hands its command line to the running
/// instance through the local bridge and exits. Works the same on Windows, macOS and Linux.
/// </summary>
public static class SingleInstance
{
    private static readonly Uri BaseUri = new($"http://127.0.0.1:{LocalBridgeServer.Port}/api/v1/");

    public static bool TryHandOff(string[] args)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = BaseUri, Timeout = TimeSpan.FromMilliseconds(1500) };
            using var statusResponse = http.GetAsync("status").GetAwaiter().GetResult();
            if (!statusResponse.IsSuccessStatusCode)
                return false;

            var status = JsonSerializer.Deserialize(
                statusResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
                BridgeJsonContext.Default.BridgeStatus);
            if (status?.App != "DownloadMaster")
                return false;

            var body = JsonSerializer.Serialize(new BridgeActivateRequest { Args = args }, BridgeJsonContext.Default.BridgeActivateRequest);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var activate = http.PostAsync("activate", content).GetAwaiter().GetResult();
            return activate.IsSuccessStatusCode;
        }
        catch
        {
            // nothing listening — this is the first instance
            return false;
        }
    }
}
