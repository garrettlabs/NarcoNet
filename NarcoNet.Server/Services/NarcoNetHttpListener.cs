using System.IO.Compression;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

using NarcoNet.Server.Models;
using NarcoNet.Server.Utilities;
using NarcoNet.Utilities;

using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers.Http;

namespace NarcoNet.Server.Services;

/// <summary>
///     HTTP listener for NarcoNet mod synchronization endpoints
/// </summary>
[Injectable(InjectionType = InjectionType.Singleton, TypePriority = OnLoadOrder.PreSptModLoader + 1)]
public class NarcoNetHttpListener(
    ILogger<NarcoNetHttpListener> logger,
    SyncService syncService,
    MimeTypeHelper mimeTypeHelper)
    : IHttpListener
{
    private NarcoNetConfig? _config;
    private volatile bool _isInitialized;
    private string? _modVersion;

    public bool CanHandle(MongoId sessionId, HttpContext context)
    {
        return context.Request.Path.StartsWithSegments("/narconet");
    }

    public async Task Handle(MongoId sessionId, HttpContext context)
    {
        if (!_isInitialized || _config == null)
        {
            var errorMsg = $"NarcoNet: Not initialized (_isInitialized={_isInitialized}, _config={(_config == null ? "null" : "not null")})";
            logger.LogWarning(errorMsg);
            context.Response.StatusCode = 500;

            byte[] errorBytes = System.Text.Encoding.UTF8.GetBytes(errorMsg);
            context.Response.ContentType = "text/plain";
            await context.Response.Body.WriteAsync(errorBytes);
            await context.Response.StartAsync();
            await context.Response.CompleteAsync();
            return;
        }

        try
        {
            string path = context.Request.Path.Value ?? "";
#if NARCONET_DEBUG_LOGGING
            logger.LogDebug("NarcoNet request received: {RequestMethod} {Path}", context.Request.Method, path);
#endif

            switch (path)
            {
                case "/narconet/version":
                    await HandleGetVersion(context);
                    break;
                case "/narconet/syncpaths":
                    await HandleGetSyncPaths(context);
                    break;
                case "/narconet/exclusions":
                    await HandleGetExclusions(context);
                    break;
                case "/narconet/ignored-profiles":
                    await HandleGetIgnoredProfiles(context);
                    break;
                case "/narconet/profile-bypass":
                    await HandleProfileBypassNotification(context);
                    break;
                case "/narconet/hashes":
                    await HandleGetHashes(context);
                    break;
                default:
                {
                    if (path.StartsWith("/narconet/fetch/"))
                    {
                        await HandleFetchModFile(context);
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                        await context.Response.WriteAsync("NarcoNet: Unknown route");
                    }

                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — already logged by the specific handler
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling request [{Method} {Path}]", context.Request.Method, context.Request.Path);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"NarcoNet: Error handling [{context.Request.Method} {context.Request.Path}]:\n{ex}");
            }
        }
    }

    public void Initialize(NarcoNetConfig config, string modVersion)
    {
        logger.LogDebug("HttpListener.Initialize() called with version {Version}", modVersion);
        _config = config;
        _modVersion = modVersion;
        _isInitialized = true;
        logger.LogDebug("HttpListener initialized successfully (_isInitialized={IsInit})", _isInitialized);
    }

    private async Task HandleGetVersion(HttpContext context)
    {
        string json = JsonSerializer.Serialize(_modVersion);
        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;
        await context.Response.Body.WriteAsync(jsonBytes);
        await context.Response.StartAsync();
        await context.Response.CompleteAsync();
    }

    private async Task HandleGetSyncPaths(HttpContext context)
    {
        var syncPaths = _config!.SyncPaths.Select(sp => new
        {
            sp.Name,
            Path = PathHelper.ToUnixPath(sp.Path),
            sp.Enabled,
            sp.Enforced,
            sp.Silent,
            sp.RestartRequired
        }).ToList();
        string json = JsonSerializer.Serialize(syncPaths);

        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;
        await context.Response.Body.WriteAsync(jsonBytes);
        await context.Response.StartAsync();
        await context.Response.CompleteAsync();
    }

    private async Task HandleGetExclusions(HttpContext context)
    {
        string json = JsonSerializer.Serialize(_config!.Exclusions);
        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;
        await context.Response.Body.WriteAsync(jsonBytes);
        await context.Response.StartAsync();
        await context.Response.CompleteAsync();
    }

    private async Task HandleGetIgnoredProfiles(HttpContext context)
    {
        string json = JsonSerializer.Serialize(_config!.IgnoredProfiles);
        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;
        await context.Response.Body.WriteAsync(jsonBytes);
        await context.Response.StartAsync();
        await context.Response.CompleteAsync();
    }

    private async Task HandleProfileBypassNotification(HttpContext context)
    {
        ProfileBypassNotification? notification = null;
        try
        {
            notification = await JsonSerializer.DeserializeAsync<ProfileBypassNotification>(context.Request.Body);
        }
        catch (JsonException e)
        {
            logger.LogWarning(e, "Received malformed NarcoNet profile bypass notification");
        }

        string? profileId = ProfileBypass.NormalizeProfileIdentifier(notification?.ProfileId);
        if (string.IsNullOrEmpty(profileId))
        {
            context.Response.StatusCode = 400;
            await context.Response.StartAsync();
            await context.Response.CompleteAsync();
            return;
        }

        logger.LogInformation("Ignoring NarcoNet sync for configured profile {ProfileId}", profileId);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;
        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes("{\"ok\":true}");
        await context.Response.Body.WriteAsync(jsonBytes);
        await context.Response.StartAsync();
        await context.Response.CompleteAsync();
    }

    private sealed record ProfileBypassNotification(string? ProfileId);

    private async Task HandleGetHashes(HttpContext context)
    {
        StringValues pathsParam = context.Request.Query["path"];

        List<SyncPath> pathsToHash;
        if (pathsParam.Count > 0)
        {
            List<string?> requestedPaths = pathsParam.ToList();
            logger.LogDebug("Client requested {Count} specific paths", requestedPaths.Count);
#if NARCONET_DEBUG_LOGGING
            foreach (var reqPath in requestedPaths)
            {
                logger.LogDebug("  - {ReqPath}", reqPath);
            }
#endif
            pathsToHash = _config!.SyncPaths
                .Where(sp => requestedPaths.Contains(sp.Path))
                .ToList();
            logger.LogDebug("Hashing {Count} paths", pathsToHash.Count);
        }
        else
        {
            logger.LogDebug("Hashing all sync paths");
            pathsToHash = _config!.SyncPaths.ToList();
        }

        Dictionary<string, Dictionary<string, ModFile>> hashResults = await syncService.HashModFilesAsync(pathsToHash, _config, context.RequestAborted);

        // Log total file counts per path
        foreach (var pathHash in hashResults)
        {
            logger.LogDebug("Path '{Path}' has {Count} files",
                pathHash.Key, pathHash.Value.Count);
        }

        string json = JsonSerializer.Serialize(hashResults);

        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;
        await context.Response.Body.WriteAsync(jsonBytes);
        await context.Response.StartAsync();
        await context.Response.CompleteAsync();
    }

    private async Task HandleFetchModFile(HttpContext context)
    {
        string pathSegment = context.Request.Path.Value?.Replace("/narconet/fetch/", "") ?? "";
        string filePath = Uri.UnescapeDataString(pathSegment);
        string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        try
        {
            string sanitizedPath = syncService.SanitizeDownloadPath(filePath, _config!.SyncPaths);

            if (!File.Exists(sanitizedPath))
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync($"Attempt to access non-existent path {filePath}");
                return;
            }

            FileInfo fileInfo = new(sanitizedPath);
            string extension = Path.GetExtension(filePath);
            string mimeType = mimeTypeHelper.GetMimeType(extension) ?? "application/octet-stream";

            // Log the download
            logger.LogInformation("Serving file '{FilePath}' ({FileSize} bytes) to {ClientIp}",
                filePath, fileInfo.Length, clientIp);

            context.Response.ContentType = mimeType;
            context.Response.StatusCode = 200;

            bool useGzip = context.Request.Headers.AcceptEncoding.ToString().Contains("gzip", StringComparison.OrdinalIgnoreCase);

            await using FileStream fileStream = new(sanitizedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);

            if (useGzip)
            {
                context.Response.Headers.ContentEncoding = "gzip";
                context.Response.Headers["X-Uncompressed-Length"] = fileInfo.Length.ToString();
                // Content-Length omitted — compressed size unknown ahead of time (chunked transfer)

                await using var gzipStream = new GZipStream(context.Response.Body, CompressionLevel.Fastest, leaveOpen: true);
                await fileStream.CopyToAsync(gzipStream, 65536, context.RequestAborted);
            }
            else
            {
                context.Response.Headers["Accept-Ranges"] = "bytes";
                context.Response.ContentLength = fileInfo.Length;

                await fileStream.CopyToAsync(context.Response.Body, 65536, context.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected mid-transfer — not a server error
            logger.LogDebug("Client {ClientIp} disconnected while downloading '{FilePath}'", clientIp, filePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading file '{FilePath}'", filePath);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"NarcoNet: Error reading '{filePath}'\n{ex}");
            }
        }
    }
}
