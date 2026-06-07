using System.Collections.Generic;

namespace LMG.Stremio.Models
{
    #region Stremio Responses

    public class StremioManifest
    {
        public string id { get; set; } = "community.lampac";
        public string version { get; set; } = "1.5.0";
        public string name { get; set; } = "Lampac";
        public string description { get; set; } = "Lampac online sources via Stremio";
        public object[] resources { get; set; } = [
            "stream",
            new {
                name = "meta",
                types = new[] { "series" },
                idPrefixes = new[] { "tt" }
            }
        ];
        public string[] types { get; set; } = ["movie", "series"];
        public string[] idPrefixes { get; set; } = ["tt"];
        public List<StremioCatalog> catalogs { get; set; } = [];
        public StremioBehaviorHints behaviorHints { get; set; } = new();
    }

    public class StremioCatalog
    {
        public string type { get; set; }
        public string id { get; set; }
        public string name { get; set; }
    }

    public class StremioBehaviorHints
    {
        public bool configurable { get; set; } = false;
    }

    public class StremioStreamResponse
    {
        public List<StremioStream> streams { get; set; } = [];
    }

    public class StremioStream
    {
        public string name { get; set; }
        public string description { get; set; }
        public string url { get; set; }
        public List<StremioSubtitle> subtitles { get; set; }
        public StremioStreamBehaviorHints behaviorHints { get; set; }
    }

    public class StremioStreamBehaviorHints
    {
        public string bingeGroup { get; set; }
    }

    public class StremioSubtitle
    {
        public string id { get; set; }
        public string url { get; set; }
        public string lang { get; set; }
    }

    #endregion

    #region TMDB Responses

    public class TmdbFindResponse
    {
        public List<TmdbMovie> movie_results { get; set; }
        public List<TmdbTv> tv_results { get; set; }
    }

    public class TmdbMovie
    {
        public int id { get; set; }
        public string title { get; set; }
        public string original_title { get; set; }
        public string release_date { get; set; }
    }

    public class TmdbTv
    {
        public int id { get; set; }
        public string name { get; set; }
        public string original_name { get; set; }
        public string first_air_date { get; set; }
    }

    #endregion

    #region Lampac Responses

    public class LampacExternalIds
    {
        public string imdb_id { get; set; }
        public string kinopoisk_id { get; set; }
    }

    public class LampacSource
    {
        public string name { get; set; }
        public string url { get; set; }
        public string balanser { get; set; }
    }

    public class LampacResponse
    {
        public string type { get; set; }
        public List<LampacVoiceItem> voice { get; set; }
        public List<object> data { get; set; }
    }

    public class LampacMovieResponse
    {
        public string type { get; set; }
        public List<LampacVoiceItem> voice { get; set; }
        public List<LampacMovieItem> data { get; set; }
    }

    public class LampacMovieItem
    {
        public string method { get; set; }
        public string url { get; set; }
        public Dictionary<string, string> quality { get; set; }
        public string translate { get; set; }
        public string maxquality { get; set; }
        public string title { get; set; }
        public int hls_manifest_timeout { get; set; }
        public List<LampacSubtitleItem> subtitles { get; set; }
        public Dictionary<string, string> headers { get; set; }
    }

    public class LampacEpisodeResponse
    {
        public string type { get; set; }
        public List<LampacVoiceItem> voice { get; set; }
        public List<LampacEpisodeItem> data { get; set; }
    }

    public class LampacEpisodeItem
    {
        public string method { get; set; }
        public string url { get; set; }
        public int s { get; set; }
        public int e { get; set; }
        public string name { get; set; }
        public string title { get; set; }
        public Dictionary<string, string> quality { get; set; }
        public List<LampacSubtitleItem> subtitles { get; set; }
    }

    public class LampacSeasonResponse
    {
        public string type { get; set; }
        public List<LampacVoiceItem> voice { get; set; }
        public List<LampacSeasonItem> data { get; set; }
    }

    public class LampacSeasonItem
    {
        public string method { get; set; }
        public int id { get; set; }
        public string url { get; set; }
        public string name { get; set; }
    }

    public class LampacSimilarResponse
    {
        public string type { get; set; }
        public List<LampacSimilarItem> data { get; set; }
    }

    public class LampacSimilarItem
    {
        public string method { get; set; }
        public string url { get; set; }
        public string title { get; set; }
        public int year { get; set; }
        public string details { get; set; }
    }

    public class LampacVideoItem
    {
        public string title { get; set; }
        public string method { get; set; }
        public string url { get; set; }
        public Dictionary<string, string> quality { get; set; }
        public List<LampacSubtitleItem> subtitles { get; set; }
        public int hls_manifest_timeout { get; set; }
    }

    public class LampacSubtitleItem
    {
        public string label { get; set; }
        public string url { get; set; }
    }

    public class LampacVoiceItem
    {
        public string method { get; set; }
        public string url { get; set; }
        public bool active { get; set; }
        public string name { get; set; }
    }

    #endregion

    #region Metadata

    public class TmdbMetadata
    {
        public int tmdb_id { get; set; }
        public string imdb_id { get; set; }
        public string kinopoisk_id { get; set; }
        public string title { get; set; }
        public string original_title { get; set; }
        public int year { get; set; }
        public int serial { get; set; }
    }

    #endregion

    #region Stremio Meta Responses

    public class StremioMetaResponse
    {
        public StremioMeta meta { get; set; }
    }

    public class StremioMeta
    {
        public string id { get; set; }
        public string type { get; set; } = "series";
        public string name { get; set; }
        public List<StremioVideo> videos { get; set; } = [];
    }

    public class StremioVideo
    {
        public string id { get; set; }
        public string title { get; set; }
        public int season { get; set; }
        public int episode { get; set; }
        public string released { get; set; }
        public List<StremioStream> streams { get; set; } = [];
    }

    #endregion

    #region TMDB TV Details

    public class TmdbTvDetails
    {
        public string name { get; set; }
        public List<TmdbTvSeason> seasons { get; set; }
    }

    public class TmdbTvSeason
    {
        public int season_number { get; set; }
        public int episode_count { get; set; }
        public string air_date { get; set; }
    }

    public class TmdbSeasonDetails
    {
        public List<TmdbEpisode> episodes { get; set; }
    }

    public class TmdbEpisode
    {
        public int episode_number { get; set; }
        public string name { get; set; }
        public string air_date { get; set; }
    }

    #endregion
}

