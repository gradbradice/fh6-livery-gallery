using LiveryGallery.Configuration;
using LiveryGallery.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LiveryGallery.Services;

internal class CarDatabaseService
{
    private static readonly string _path = Path.Combine(AppSettings.BaseCachePath, "fh6_car_id.json");
    private const string _dbUrl =
        "https://github.com/gradbradice/forza-car-id/raw/refs/heads/main/fh6_car_id.json";

    private readonly HttpClient _http;
    private Dictionary<int, CarInfo> _byId = [];
    public int Count => _byId.Count;
    public bool HasLocalData { get; private set; }
    public string? LastError { get; private set; }

    public CarDatabaseService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FH6-Livery-Gallery/1.0");
    }

    public CarInfo? Get(int carId) => _byId.TryGetValue(carId, out var c) ? c : null;

    public void LoadLocal()
    {
        if (!File.Exists(_path)) return;
        try
        {
            string json = File.ReadAllText(_path);
            var list = ParseJson(json);
            if (list.Count > 0)
            {
                _byId = BuildIndex(list);
                HasLocalData = true;
            }
        }
        catch
        {
            
        }
    }

    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            string json = await _http.GetStringAsync(_dbUrl, ct);
            var list = ParseJson(json);
            if (list.Count == 0)
            {
                LastError = "empty or invalid response";
                return false;
            }

            bool changed = true;
            if (File.Exists(_path))
            {
                try
                {
                    string existing = await File.ReadAllTextAsync(_path, ct);
                    changed = !ContentHashEquals(existing, json);
                }
                catch
                {
                    changed = true;
                }
            }

            if (changed)
            {
                await File.WriteAllTextAsync(_path, json, ct);
            }

            _byId = BuildIndex(list);
            HasLocalData = true;
            LastError = null;
            return changed;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static Dictionary<int, CarInfo> BuildIndex(List<CarInfo> list)
    {
        var dict = new Dictionary<int, CarInfo>(list.Count);
        foreach (var c in list)
            dict.TryAdd(c.Id, c);
        return dict;
    }

    private static List<CarInfo> ParseJson(string json)
    {
        var raw = JsonSerializer.Deserialize<List<CarInfoEntry>>(json, JsonSettings.GitHubDeserializeOptions);
        if (raw is null) return [];

        var result = new List<CarInfo>(raw.Count);
        foreach (var r in raw)
        {
            if (string.IsNullOrWhiteSpace(r.Manufacturer) 
                || string.IsNullOrWhiteSpace(r.Name)
                || r.Year == null) continue;
            result.Add(new CarInfo
            {
                Id = r.Id,
                Manufacturer = r.Manufacturer,
                Name = r.Name,
                Year = (int)r.Year,
            });
        }
        return result;
    }

    private static bool ContentHashEquals(string a, string b)
    {
        Span<byte> ha = stackalloc byte[32];
        Span<byte> hb = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(a), ha);
        SHA256.HashData(Encoding.UTF8.GetBytes(b), hb);
        return ha.SequenceEqual(hb);
    }
}
