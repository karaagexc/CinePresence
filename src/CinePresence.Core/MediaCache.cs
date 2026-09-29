using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CinePresence.Core;

public sealed class MediaCache
{
    private sealed record Entry(ResolvedMedia Media, DateTimeOffset Expires);
    private sealed class Document
    {
        public Dictionary<string, Entry> Matches { get; set; } = [];
        public Dictionary<string, ResolvedMedia> Overrides { get; set; } = [];
    }
    private readonly object gate = new();
    private readonly string? path;
    private Document data = new();

    public MediaCache(string? path = null)
    {
        this.path = path;
        if (path is null || !File.Exists(path)) return;
        try { data = JsonSerializer.Deserialize<Document>(File.ReadAllText(path)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }

    public ResolvedMedia? Get(string key, DateTimeOffset now, bool allowExpired = false)
    {
        lock (gate)
        {
            var hash = Hash(key);
            if (data.Overrides.TryGetValue(hash, out var correction)) return correction;
            return data.Matches.TryGetValue(hash, out var entry) && (allowExpired || entry.Expires > now) ? entry.Media : null;
        }
    }

    public void Put(string key, ResolvedMedia media, DateTimeOffset expires, bool correction = false)
    {
        lock (gate)
        {
            if (correction) data.Overrides[Hash(key)] = media;
            else data.Matches[Hash(key)] = new(media, expires);
            while (data.Matches.Count > 500) data.Matches.Remove(data.Matches.MinBy(x => x.Value.Expires).Key);
            Save();
        }
    }

    public void Clear()
    {
        lock (gate) { data = new(); Save(); }
    }

    private void Save()
    {
        if (path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new ServiceException("The local match cache could not be saved. Check folder permissions."); }
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
