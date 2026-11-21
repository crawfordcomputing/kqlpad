using System.IO.Compression;
using System.Text;
using KqlPad.Models;

namespace KqlPad.Services;

public static class PortalLink
{
    /// <summary>
    /// Builds an Azure portal deep link that opens the Logs blade for the workspace with the query pre-filled
    /// (the same gzip + base64 encoding the portal's "Share link to query" uses).
    /// Returns null when the profile has no workspace resource ID.
    /// </summary>
    public static string? Build(ClientProfile profile, string kql)
    {
        if (string.IsNullOrWhiteSpace(profile.WorkspaceResourceId)) return null;

        byte[] gz;
        using (var ms = new MemoryStream())
        {
            using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                var bytes = Encoding.UTF8.GetBytes(kql);
                gzip.Write(bytes, 0, bytes.Length);
            }
            gz = ms.ToArray();
        }

        var query = Uri.EscapeDataString(Convert.ToBase64String(gz));
        var resource = Uri.EscapeDataString(profile.WorkspaceResourceId.Trim());
        var tenant = string.IsNullOrWhiteSpace(profile.TenantId) ? "" : "@" + profile.TenantId.Trim();
        return $"https://portal.azure.com/#{tenant}/blade/Microsoft_Azure_Monitoring_Logs/LogsBlade/resourceId/{resource}" +
               $"/source/LogsBlade.AnalyticsShareLinkToQuery/q/{query}/timespan/P30D";
    }
}
