using LiveryGallery.Configuration;
using LiveryGallery.Enums;
using LiveryGallery.Models;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LiveryGallery.Services;

internal class CarDatabaseService
{
    private static readonly string _path = Path.Combine(AppSettings.BaseCachePath, "fh6_car_id.json");
    private static readonly string _metaPath = Path.Combine(AppSettings.BaseCachePath, "fh6_car_id.meta.json");
    private const string _dbUrl =
        "https://raw.githubusercontent.com/gradbradice/forza-car-id/main/fh6_car_id.json";

    private readonly HttpClient _http;
    private readonly Lock _lock = new();
    private Dictionary<int, CarInfo> _byId = [];
    private CarDatabaseMetadata _metadata = new();

    private Task<CarDatabaseRefreshOutcome>? _inFlightRefresh;
    private readonly object _refreshGate = new();

    private bool _hasLocalData;
    private string? _lastError;

    public int Count { get { lock (_lock) return _byId.Count; } }
    public bool HasLocalData { get { lock (_lock) return _hasLocalData; } private set { lock (_lock) _hasLocalData = value; } }
    public string? LastError { get { lock (_lock) return _lastError; } private set { lock (_lock) _lastError = value; } }

    public CarDatabaseService(HttpClient http)
    {
        _http = http;
    }

    public CarInfo? Get(int carId)
    {
        lock (_lock) return _byId.TryGetValue(carId, out var c) ? c : null;
    }

    public void LoadLocal()
    {
        lock (_lock) _metadata = LoadMetadata();

        if (!File.Exists(_path)) return;
        try
        {
            string json = File.ReadAllText(_path);
            var (list, _) = ParseJson(json);
            if (list.Count > 0)
            {
                string actualHash = ComputeSemanticHash(list);
                lock (_lock)
                {
                    if (_metadata.ContentHash != actualHash)
                    {
                        _metadata = new CarDatabaseMetadata
                        {
                            ETag = null,
                            ContentHash = actualHash,
                            Count = list.Count,
                            LastCheckedUtc = _metadata.LastCheckedUtc,
                        };
                    }
                    _byId = BuildIndex(list);
                }
                HasLocalData = true;
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AppLogger.LogError("Failed to load the local car database", ex);
        }
    }

    public Task<CarDatabaseRefreshOutcome> RefreshAsync(CancellationToken ct = default)
    {
        lock (_refreshGate)
        {
            _inFlightRefresh ??= RefreshCoreAsync(ct);
            return _inFlightRefresh;
        }
    }

    private async Task<CarDatabaseRefreshOutcome> RefreshCoreAsync(CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

            string? currentETag;
            lock (_lock) currentETag = _metadata.ETag;

            using var request = new HttpRequestMessage(HttpMethod.Get, _dbUrl);
            if (!string.IsNullOrEmpty(currentETag) && File.Exists(_path))
                request.Headers.TryAddWithoutValidation("If-None-Match", currentETag);

            using var response = await _http.SendAsync(request, timeoutCts.Token);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                lock (_lock) _metadata.LastCheckedUtc = DateTime.UtcNow;
                await SaveMetadataAsync();
                LastError = null;
                return CarDatabaseRefreshOutcome.NotModified;
            }

            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            string? newETag = response.Headers.ETag?.Tag;

            var (list, rejectedCount) = ParseJson(json);
            if (list.Count == 0)
            {
                LastError = "empty or invalid response";
                return CarDatabaseRefreshOutcome.Failed;
            }

            string? validationError = Validate(list, rejectedCount);
            if (validationError is not null)
            {
                LastError = validationError;
                AppLogger.LogErrorThrottled(
                    newETag ?? "unknown",
                    "Downloaded car database failed validation, keeping the current one",
                    new InvalidDataException(validationError));
                return CarDatabaseRefreshOutcome.Failed;
            }

            string semanticHash = ComputeSemanticHash(list);
            bool changed;
            lock (_lock) changed = semanticHash != _metadata.ContentHash;

            if (changed)
            {
                await AtomicFile.WriteAllTextAsync(_path, json, ct);
            }

            lock (_lock)
            {
                _metadata = new CarDatabaseMetadata
                {
                    ETag = newETag,
                    ContentHash = semanticHash,
                    Count = list.Count,
                    LastCheckedUtc = DateTime.UtcNow,
                };
                _byId = BuildIndex(list);
            }

            bool metadataSaved = await SaveMetadataAsync();
            if (!metadataSaved)
            {
                AppLogger.LogError(
                    "Car database was updated, but failed to persist refresh metadata. ETag will be re-fetched next time",
                    new IOException("SaveMetadataAsync failed after a successful database update"));
            }

            HasLocalData = true;
            LastError = null;
            return changed ? CarDatabaseRefreshOutcome.Updated : CarDatabaseRefreshOutcome.NotModified;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AppLogger.LogError("Failed to update the car database from GitHub", ex);
            return CarDatabaseRefreshOutcome.Failed;
        }
        finally
        {
            lock (_refreshGate) _inFlightRefresh = null;
        }
    }

    private string? Validate(List<CarInfo> list, int rejectedCount)
    {
        int totalSeen = list.Count + rejectedCount;
        if (totalSeen > 0 && rejectedCount > totalSeen * 0.1)
            return $"too many invalid entries while parsing: {rejectedCount} of {totalSeen}";

        var duplicateIds = list
            .GroupBy(c => c.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateIds.Count > 0)
        {
            string ids = string.Join(", ", duplicateIds.Take(5));
            if (duplicateIds.Count > 5) ids += ", ...";
            return $"duplicate car IDs: {ids}";
        }

        var invalidIds = list.Where(c => c.Id <= 0).Select(c => c.Id).ToList();
        if (invalidIds.Count > 0)
        {
            string ids = string.Join(", ", invalidIds.Take(5));
            if (invalidIds.Count > 5) ids += ", ...";
            return $"invalid car IDs (<= 0): {ids}";
        }

        int oldCount;
        lock (_lock) oldCount = _byId.Count;
        if (oldCount > 0 && list.Count < oldCount * 0.9)
            return $"suspicious count drop: {oldCount} -> {list.Count}";

        return null;
    }

    private static CarDatabaseMetadata LoadMetadata()
    {
        try
        {
            if (!File.Exists(_metaPath)) return new CarDatabaseMetadata();
            string json = File.ReadAllText(_metaPath);
            var data = JsonSerializer.Deserialize<CarDatabaseMetadata>(json, JsonSettings.DefaultOptions);
            if (data is null) return new CarDatabaseMetadata();

            if (data.ETag is not null && data.ContentHash is null)
            {
                AppLogger.LogError(
                    "Car database metadata is inconsistent, resetting",
                    new InvalidDataException("ETag present without ContentHash"));
                return new CarDatabaseMetadata();
            }
            return data;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to load car database metadata", ex);
            return new CarDatabaseMetadata();
        }
    }

    private async Task<bool> SaveMetadataAsync()
    {
        try
        {
            CarDatabaseMetadata snapshot;
            lock (_lock) snapshot = _metadata;
            string json = JsonSerializer.Serialize(snapshot, JsonSettings.DefaultOptions);
            await AtomicFile.WriteAllTextAsync(_metaPath, json);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to save car database metadata", ex);
            return false;
        }
    }

    private static Dictionary<int, CarInfo> BuildIndex(List<CarInfo> list)
    {
        var dict = new Dictionary<int, CarInfo>(list.Count);
        foreach (var c in list)
            dict.Add(c.Id, c);
        return dict;
    }

    private static (List<CarInfo> Valid, int RejectedCount) ParseJson(string json)
    {
        var raw = JsonSerializer.Deserialize<List<CarInfoEntry>>(json, JsonSettings.GitHubDeserializeOptions);
        if (raw is null) return ([], 0);

        var result = new List<CarInfo>(raw.Count);
        int rejected = 0;
        foreach (var r in raw)
        {
            if (string.IsNullOrWhiteSpace(r.Manufacturer)
                || string.IsNullOrWhiteSpace(r.Name)
                || r.Year == null)
            {
                rejected++;
                continue;
            }
            result.Add(new CarInfo
            {
                Id = r.Id,
                Manufacturer = r.Manufacturer,
                Name = r.Name,
                Year = (int)r.Year,
            });
        }
        return (result, rejected);
    }

    private static string ComputeSemanticHash(List<CarInfo> list)
    {
        var sb = new StringBuilder();
        foreach (var c in list.OrderBy(x => x.Id))
        {
            sb.Append(c.Id).Append('|')
              .Append(c.Manufacturer).Append('|')
              .Append(c.Name).Append('|')
              .Append(c.Year).Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
