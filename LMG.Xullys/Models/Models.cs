using System.Collections.Generic;

namespace LMG.Xullys.Models;

/// <summary>
/// Елемент downloads з API Xullys
/// </summary>
public class XullysDownload
{
    public string id { get; set; }
    public string url { get; set; }
    public string source { get; set; }
    public string quality { get; set; }
    public string type { get; set; }
    public string size { get; set; }
    public string fileName { get; set; }

    /// <summary>
    /// Мітка для списку потоків: quality • size • source
    /// </summary>
    public string BuildLabel()
    {
        string quality = string.IsNullOrWhiteSpace(this.quality) ? "auto" : this.quality.Trim();
        string size = (this.size ?? string.Empty).Trim();
        string source = (this.source ?? string.Empty).Trim();

        var parts = new List<string> { quality };
        if (!string.IsNullOrEmpty(size))
            parts.Add(size);
        if (!string.IsNullOrEmpty(source))
            parts.Add(source);

        return string.Join(" • ", parts);
    }
}

/// <summary>
/// Відповідь API Xullys
/// </summary>
public class XullysResponse
{
    public bool ok { get; set; }
    public List<XullysDownload> downloads { get; set; }
    public List<object> subtitles { get; set; }
}

/// <summary>
/// Сезон серіалу (структура з TMDB)
/// </summary>
public class XullysSeason
{
    public int SeasonNumber { get; set; }
    public int EpisodeCount { get; set; }
}
