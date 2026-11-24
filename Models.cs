using System.Text.Json.Nodes;

namespace NcmNative;

public sealed class Song
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Cover { get; set; } = "";
    public long DurMs { get; set; }
    public int Fee { get; set; }

    public string Dur => TimeSpan.FromMilliseconds(DurMs <= 0 ? 0 : DurMs).ToString(@"mm\:ss");
    public bool Vip => Fee == 1 || Fee == 4;

    public static Song FromJson(JsonNode n)
    {
        if (n == null) return null;
        var ar = n["ar"] ?? n["artists"];
        var al = n["al"] ?? n["album"];

        var artists = new List<string>();
        if (ar is JsonArray arr)
            foreach (var a in arr)
            {
                var nm = (a?["name"]).AsStr();
                if (!string.IsNullOrEmpty(nm)) artists.Add(nm);
            }

        return new Song
        {
            Id = n["id"].AsLong(),
            Name = n["name"].AsStr(),
            Artist = artists.Count > 0 ? string.Join(" / ", artists) : "未知",
            Album = (al?["name"]).AsStr(),
            Cover = (al?["picUrl"]).AsStr(),
            DurMs = n["dt"].AsLong() is var dt && dt > 0 ? dt : n["duration"].AsLong(),
            Fee = n["fee"].AsInt(),
        };
    }
}

public sealed class Playlist
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Cover { get; set; } = "";
    public long PlayCount { get; set; }
    public int Count { get; set; }
}
