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
            _logger.Info("Nyaa SeasonSearchCriteria: Series={0}, Season={1}, Year={2}, SceneTitles=[{3}]",
                searchCriteria.Series.Title,
                searchCriteria.SeasonNumber,
                searchCriteria.Series.Year,
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

            // New pattern: <Nombre serie> <Year> (only if series has a year)
            if (searchCriteria.Series.Year > 0)
            {
                foreach (var searchTitle in searchCriteria.SceneTitles.Select(PrepareQuery))
                {
                    pageableRequests.Add(GetPagedRequests($"{searchTitle}+{searchCriteria.Series.Year}"));
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
            var pageableRequests = new IndexerPageableRequestChain();

            _logger.Info("Nyaa AnimeSeasonSearch: Series={0}, Season={1}, Year={2}, SceneTitles=[{3}]",
                searchCriteria.Series.Title,
                searchCriteria.SeasonNumber,
                searchCriteria.Series.Year,
                string.Join(", ", searchCriteria.SceneTitles));

            foreach (var searchTitle in searchCriteria.SceneTitles.Select(PrepareQuery))
            {
                var titleTrimmed = Regex.Replace(searchTitle, @"[-,.\?!:;/]", "+").Trim();

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

                // New pattern: <Nombre serie> <Year> (only if series has a year)
                if (searchCriteria.Series.Year > 0)
                {
                    var yearPattern = $"{titleTrimmed}+{searchCriteria.Series.Year}";
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
