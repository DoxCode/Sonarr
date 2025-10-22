using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.Nyaa
{
    public class NyaaRequestGenerator : IIndexerRequestGenerator
    {
        private readonly Logger _logger;
        public NyaaSettings Settings { get; set; }

        public NyaaRequestGenerator()
        {
            _logger = NzbDroneLogger.GetLogger(GetType());
        }

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(null));

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(SingleEpisodeSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            if (Settings.AnimeStandardFormatSearch && searchCriteria.SeasonNumber > 0 && searchCriteria.EpisodeNumber > 0)
            {
                foreach (var searchTitle in searchCriteria.SceneTitles.Select(PrepareQuery))
                {
                    pageableRequests.Add(GetPagedRequests($"{searchTitle}+s{searchCriteria.SeasonNumber:00}e{searchCriteria.EpisodeNumber:00}"));
                }
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(SeasonSearchCriteria searchCriteria)
        {
            var seasonYear = searchCriteria.SeasonYear ?? searchCriteria.Series.Year;
            _logger.Info("Nyaa SeasonSearchCriteria: Series={0}, Season={1}, Year={2}, SeasonYear={3}, SceneTitles=[{4}]",
                searchCriteria.Series.Title,
                searchCriteria.SeasonNumber,
                searchCriteria.Series.Year,
                seasonYear,
                string.Join(", ", searchCriteria.SceneTitles));

            var pageableRequests = new IndexerPageableRequestChain();

            if (Settings.AnimeStandardFormatSearch && searchCriteria.SeasonNumber > 0)
            {
                foreach (var searchTitle in searchCriteria.SceneTitles.Select(PrepareQuery))
                {
                    // Original pattern: <Nombre serie> sXX
                    pageableRequests.Add(GetPagedRequests($"{searchTitle}+s{searchCriteria.SeasonNumber:00}"));

                    // New pattern: <Nombre serie> Season XX
                    pageableRequests.Add(GetPagedRequests($"{searchTitle}+Season+{searchCriteria.SeasonNumber}"));
                }
            }

            // New pattern: <Nombre serie> <Year> (only if season has a year)
            if (seasonYear > 0)
            {
                foreach (var searchTitle in searchCriteria.SceneTitles.Select(PrepareQuery))
                {
                    pageableRequests.Add(GetPagedRequests($"{searchTitle}+{seasonYear}"));
                }
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(DailyEpisodeSearchCriteria searchCriteria)
        {
            return new IndexerPageableRequestChain();
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(DailySeasonSearchCriteria searchCriteria)
        {
            return new IndexerPageableRequestChain();
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(AnimeEpisodeSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            foreach (var searchTitle in searchCriteria.SceneTitles.Select(PrepareQuery))
            {
                if (searchCriteria.AbsoluteEpisodeNumber > 0)
                {
                    pageableRequests.Add(GetPagedRequests($"{searchTitle}+{searchCriteria.AbsoluteEpisodeNumber:0}"));

                    if (searchCriteria.AbsoluteEpisodeNumber < 10)
                    {
                        pageableRequests.Add(GetPagedRequests($"{searchTitle}+{searchCriteria.AbsoluteEpisodeNumber:00}"));
                    }
                }

                if (Settings.AnimeStandardFormatSearch && searchCriteria.SeasonNumber > 0 && searchCriteria.EpisodeNumber > 0)
                {
                    pageableRequests.Add(GetPagedRequests($"{searchTitle}+s{searchCriteria.SeasonNumber:00}e{searchCriteria.EpisodeNumber:00}"));
                }
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(AnimeSeasonSearchCriteria searchCriteria)
        {
            var seasonYear = searchCriteria.SeasonYear ?? searchCriteria.Series.Year;
            var pageableRequests = new IndexerPageableRequestChain();

            _logger.Info("Nyaa AnimeSeasonSearch: Series={0}, Season={1}, Year={2}, SeasonYear={3}, Part={4}/{5}, SceneTitles=[{6}]",
                searchCriteria.Series.Title,
                searchCriteria.SeasonNumber,
                searchCriteria.Series.Year,
                seasonYear,
                searchCriteria.SeasonPartNumber,
                searchCriteria.TotalSeasonParts,
                string.Join(", ", searchCriteria.SceneTitles));

            foreach (var searchTitle in searchCriteria.SceneTitles.Select(PrepareQuery))
            {
                var titleTrimmed = Regex.Replace(searchTitle, @"[-,.\?!:;/]", "+").Trim();

                // If this is a multi-part search, add Part-specific patterns first
                if (searchCriteria.SeasonPartNumber > 0 && searchCriteria.TotalSeasonParts > 1)
                {
                    var partKeyword = $"Part+{searchCriteria.SeasonPartNumber}";

                    if (Settings.AnimeStandardFormatSearch && searchCriteria.SeasonNumber > 0)
                    {
                        // Pattern: <Series> Part 1 sXX
                        var partSeasonPattern = $"{titleTrimmed}+{partKeyword}+s{searchCriteria.SeasonNumber:00}";
                        _logger.Info("Nyaa AnimeSeasonSearch: Adding part+season pattern: {0}", partSeasonPattern);
                        pageableRequests.Add(GetPagedRequests(partSeasonPattern));

                        // Pattern: <Series> Part 1 Season XX
                        var partSeasonAltPattern = $"{titleTrimmed}+{partKeyword}+Season+{searchCriteria.SeasonNumber}";
                        _logger.Info("Nyaa AnimeSeasonSearch: Adding part+season (alt) pattern: {0}", partSeasonAltPattern);
                        pageableRequests.Add(GetPagedRequests(partSeasonAltPattern));
                    }

                    // Pattern: <Series> Part 1 (for first season only)
                    if (searchCriteria.SeasonNumber == 1)
                    {
                        var partOnlyPattern = $"{titleTrimmed}+{partKeyword}";
                        _logger.Info("Nyaa AnimeSeasonSearch: Adding part-only pattern: {0}", partOnlyPattern);
                        pageableRequests.Add(GetPagedRequests(partOnlyPattern));
                    }

                    // Pattern: <Series> Part 1 <Year> (if season has a year)
                    if (seasonYear > 0)
                    {
                        var partYearPattern = $"{titleTrimmed}+{partKeyword}+{seasonYear}";
                        _logger.Info("Nyaa AnimeSeasonSearch: Adding part+year pattern: {0}", partYearPattern);
                        pageableRequests.Add(GetPagedRequests(partYearPattern));
                    }
                }

                // Add standard patterns as fallback
                if (Settings.AnimeStandardFormatSearch && searchCriteria.SeasonNumber > 0)
                {
                    // Original pattern: <Nombre serie> sXX
                    var originalPattern = $"{titleTrimmed}+s{searchCriteria.SeasonNumber:00}";
                    _logger.Info("Nyaa AnimeSeasonSearch: Adding original pattern: {0}", originalPattern);
                    pageableRequests.Add(GetPagedRequests(originalPattern));

                    // New pattern: <Nombre serie> Season XX
                    var seasonPattern = $"{titleTrimmed}+Season+{searchCriteria.SeasonNumber}";
                    _logger.Info("Nyaa AnimeSeasonSearch: Adding season pattern: {0}", seasonPattern);
                    pageableRequests.Add(GetPagedRequests(seasonPattern));

                    if (searchCriteria.SeasonNumber == 1)
                    {
                        var firstSeason = $"{titleTrimmed}";
                        pageableRequests.Add(GetPagedRequests(firstSeason));
                        _logger.Info("Nyaa AnimeSeasonSearch: Adding first season pattern: {0}", firstSeason);
                    }
                }

                // New pattern: <Nombre serie> <Year> (now uses season year if available)
                if (seasonYear > 0)
                {
                    var yearPattern = $"{titleTrimmed}+{seasonYear}";
                    _logger.Info("Nyaa AnimeSeasonSearch: Adding year pattern: {0}", yearPattern);
                    pageableRequests.Add(GetPagedRequests(yearPattern));
                }
            }

            _logger.Info("Nyaa AnimeSeasonSearch: Generated {0} search requests in {1} tiers", pageableRequests.GetAllTiers().Count(), pageableRequests.Tiers);
            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(SpecialEpisodeSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            foreach (var queryTitle in searchCriteria.EpisodeQueryTitles)
            {
                pageableRequests.Add(GetPagedRequests(PrepareQuery(queryTitle)));
            }

            return pageableRequests;
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(string term)
        {
            var baseUrl = $"{Settings.BaseUrl.TrimEnd('/')}/?page=rss{Settings.AdditionalParameters}";

            if (term != null)
            {
                baseUrl += "&term=" + term;
            }

            yield return new IndexerRequest(baseUrl, HttpAccept.Rss);
        }

        private string PrepareQuery(string query)
        {
            return query.Replace(' ', '+');
        }
    }
}
