using System.Net;
using System.Net.Http;

using NarcoNet.Utilities;

using SPT.Common.Http;
using SPT.Common.Utils;

namespace NarcoNet;

/// <summary>
///     Handles communication with the NarcoNet server
/// </summary>
public class ServerModule
{
    private readonly HttpClient _httpClient;

    public ServerModule(Version pluginVersion)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.Add("narconet-version", pluginVersion.ToString());
    }

    private async Task<string> GetJsonTask(string jsonPath)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
#if NARCONET_DEBUG_LOGGING
            NarcoPlugin.Logger.LogDebug($"GetJsonTask: Requesting {RequestHandler.Host}{jsonPath}");
#endif
            HttpResponseMessage response = await _httpClient.GetAsync($"{RequestHandler.Host}{jsonPath}", cts.Token);
            response.EnsureSuccessStatusCode();
            string jsonResponse = await response.Content.ReadAsStringAsync();
            return jsonResponse;
        }
        catch (Exception e)
        {
            NarcoPlugin.Logger.LogError($"Request failed for {jsonPath}");
            NarcoPlugin.Logger.LogError($"  Exception Type: {e.GetType().FullName}");
            NarcoPlugin.Logger.LogError($"  Message: {(string.IsNullOrEmpty(e.Message) ? "<empty>" : e.Message)}");
            NarcoPlugin.Logger.LogError($"  URL: {RequestHandler.Host}{jsonPath}");

            if (e is HttpRequestException)
            {
#if NARCONET_DEBUG_LOGGING
                NarcoPlugin.Logger.LogDebug($"  HTTP Request Exception Details: {e}");
#endif
            }

            if (e.InnerException != null)
            {
                NarcoPlugin.Logger.LogError($"  Inner Exception: {e.InnerException.GetType().FullName}");
                NarcoPlugin.Logger.LogError($"  Inner Message: {(string.IsNullOrEmpty(e.InnerException.Message) ? "<empty>" : e.InnerException.Message)}");
            }

            NarcoPlugin.Logger.LogError($"  Stack Trace: {e.StackTrace}");
            throw;
        }
    }

    internal async Task DownloadFile(string file, string path, SemaphoreSlim limiter, CancellationToken cancellationToken, IProgress<(long bytesDownloaded, long totalBytes)>? byteProgress = null)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // file is already gameroot-relative with forward slashes
        string downloadPath = Path.GetFullPath(Path.Combine(path, file.Replace('\\', '/')));

        VFS.CreateDirectory(downloadPath.GetDirectory());

        var retryCount = 0;

        await limiter.WaitAsync(cancellationToken);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // URL-encode the file path to preserve ../ and other special characters
                    string encodedPath = Uri.EscapeDataString(file.Replace("\\", "/"));

                    // Use SendAsync with ResponseHeadersRead to get headers before streaming
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"{RequestHandler.Host}/narconet/fetch/{encodedPath}");
                    using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    // Prefer X-Uncompressed-Length (sent by server for gzip responses) for
                    // accurate progress; fall back to Content-Length for uncompressed responses.
                    long totalBytes = -1;
                    if (response.Headers.TryGetValues("X-Uncompressed-Length", out var uncompressedValues) &&
                        long.TryParse(uncompressedValues.FirstOrDefault(), out long uncompressedLength))
                    {
                        totalBytes = uncompressedLength;
                    }
                    else
                    {
                        totalBytes = response.Content.Headers.ContentLength ?? -1;
                    }

                    if (byteProgress != null && totalBytes > 0)
                    {
                        byteProgress.Report((0, totalBytes));
                    }

                    using (Stream responseStream = await response.Content.ReadAsStreamAsync())
                    using (FileStream filestream = new(downloadPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                    {
                        // Manual read loop with per-read activity timeout (30s).
                        // If no data arrives for 30s the download is considered stalled.
                        byte[] buffer = new byte[81920];
                        long bytesDownloaded = 0;
                        int bytesRead;
                        do
                        {
                            using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                            {
                                readCts.CancelAfter(TimeSpan.FromSeconds(30));
                                bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length, readCts.Token);
                            }
                            if (bytesRead > 0)
                            {
                                await filestream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                                bytesDownloaded += bytesRead;
                                if (byteProgress != null && totalBytes > 0)
                                {
                                    byteProgress.Report((bytesDownloaded, totalBytes));
                                }
                            }
                        } while (bytesRead > 0);
                        NarcoPlugin.Logger.LogInfo($"Downloaded: {file} ({bytesDownloaded} bytes)");
                    }

                    return;
                }
                catch (Exception e)
                {
                    if (e is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    retryCount++;
                    await Task.Delay(1000 * retryCount, cancellationToken);
                    switch (retryCount)
                    {
                        case >= 1 and <= 5:
                            int retryTime = 2 * retryCount;
                            NarcoPlugin.Logger.LogDebug(
                                $"Download failed for '{file}', retrying in {retryTime} seconds (Attempt {retryCount}/5)");
#if NARCONET_DEBUG_LOGGING
                            NarcoPlugin.Logger.LogDebug($"  Exception: {e.GetType().FullName}: {(string.IsNullOrEmpty(e.Message) ? "<empty>" : e.Message)}");
#endif
                            break;
                        case > 5:
                            NarcoPlugin.Logger.LogError($"Download failed for '{file}' after {retryCount} attempts");
                            NarcoPlugin.Logger.LogError($"  Exception Type: {e.GetType().FullName}");
                            NarcoPlugin.Logger.LogError($"  Message: {(string.IsNullOrEmpty(e.Message) ? "<empty>" : e.Message)}");

                            if (e.InnerException != null)
                            {
                                NarcoPlugin.Logger.LogError($"  Inner Exception: {e.InnerException.GetType().FullName}");
                                NarcoPlugin.Logger.LogError($"  Inner Message: {(string.IsNullOrEmpty(e.InnerException.Message) ? "<empty>" : e.InnerException.Message)}");
                            }

                            NarcoPlugin.Logger.LogError($"  Stack Trace: {e.StackTrace}");
                            throw;
                    }
                }
            }
        }
        finally
        {
            limiter.Release();
        }
    }

    internal async Task<string> GetNarcoNetVersion()
    {
        return Json.Deserialize<string>(await GetJsonTask("/narconet/version"));
    }

    internal async Task<List<SyncPath>> GetLocalSyncPaths()
    {
        return Json.Deserialize<List<SyncPath>>(await GetJsonTask("/narconet/syncpaths"));
    }

    internal async Task<List<string>> GetListExclusions()
    {
        return Json.Deserialize<List<string>>(await GetJsonTask("/narconet/exclusions"));
    }

    internal async Task<Dictionary<string, Dictionary<string, ModFile>>> GetRemoteHashes(List<SyncPath> paths)
    {
        if (paths.Count == 0)
        {
            NarcoPlugin.Logger.LogWarning("No sync paths provided");
            return new Dictionary<string, Dictionary<string, ModFile>>();
        }

        try
        {
            string queryString = string.Join("&path=", paths.Select(path => Uri.EscapeDataString(path.Path.Replace(@"\", "/"))));
#if NARCONET_DEBUG_LOGGING
            NarcoPlugin.Logger.LogDebug($"GetRemoteHashes: Requesting hashes for {paths.Count} paths");
            NarcoPlugin.Logger.LogDebug($"  Query: /narconet/hashes?path={queryString}");
#endif
            string json = await GetJsonTask($"/narconet/hashes?path={queryString}");

#if NARCONET_DEBUG_LOGGING
            NarcoPlugin.Logger.LogDebug($"GetRemoteHashes: Received JSON response ({json.Length} bytes)");
#endif

            var rawData =
                Json.Deserialize<Dictionary<string, Dictionary<string, ModFile>>>(json);

            return rawData.ToDictionary(
                item => item.Key,
                item => new Dictionary<string, ModFile>(item.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
            NarcoPlugin.Logger.LogError($"Failed to get remote hashes");
            NarcoPlugin.Logger.LogError($"  Exception Type: {e.GetType().FullName}");
            NarcoPlugin.Logger.LogError($"  Message: {(string.IsNullOrEmpty(e.Message) ? "<empty>" : e.Message)}");

            if (e.InnerException != null)
            {
                NarcoPlugin.Logger.LogError($"  Inner Exception: {e.InnerException.GetType().FullName}");
                NarcoPlugin.Logger.LogError($"  Inner Message: {(string.IsNullOrEmpty(e.InnerException.Message) ? "<empty>" : e.InnerException.Message)}");
            }

            NarcoPlugin.Logger.LogError($"  Stack Trace: {e.StackTrace}");
            throw;
        }
    }

}
