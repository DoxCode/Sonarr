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

            // Checkear searchCriteria.SceneTitles, transformarlo a minusculas y comprobar que no hay repetidos
            var filteredTitles = searchCriteria.SceneTitles
                .Select(title => Regex.Replace(title.ToLowerInvariant(), @"[-,.\?!:;/()'\""]+", "+").Trim())
                .Distinct()
                .ToList();

            var endTitles = new List<string>();

            var numberEpisodesSeason = searchCriteria.Episodes.Count;
            var partsEpisodes = new List<int> { 0 };

            partsEpisodes.AddRange(searchCriteria.Episodes.Where(episode => episode.FinaleType == "midseason" || episode.FinaleType == "season" || episode.FinaleType == "series").Select(e => e.EpisodeNumber).ToList());

            if (partsEpisodes.Count <= 2)
            {
                filteredTitles = filteredTitles.Where(t => !Regex.IsMatch(t.ToLowerInvariant(), @"part\+\d+")).ToList();
            }

            var partFormed = false;

            foreach (var searchTitle in filteredTitles.Select(PrepareQuery))
            {
                if (Settings.AnimeStandardFormatSearch && searchCriteria.SeasonNumber > 0)
                {
                    if (searchTitle.ToUpper().Contains(" S" + searchCriteria.SeasonNumber))
                    {
                        // Original pattern: <Nombre serie> sXX
                        var originalPattern = $"{searchTitle}+s{searchCriteria.SeasonNumber:00}";

                        // pageableRequests.Add(GetPagedRequests(originalPattern));
                        endTitles.Add(originalPattern);
                    }

                    if (searchTitle.ToLower().Contains("part") && !partFormed)
                    {
                        var partMatch = Regex.Match(searchTitle.ToLower(), @"\+part\+(\d+)");
                        if (partMatch.Success)
                        {
                            var partNumber = int.Parse(partMatch.Groups[1].Value);

                            var titleWithoutPart = Regex.Replace(searchTitle.ToLower(), @"\+part\+\d+", "");

                            if (partsEpisodes.Count > 2)
                            {
                                for (var itr = 1; itr < partsEpisodes.Count; itr++)
                                {
                                    var ep = partsEpisodes[itr] - partsEpisodes[itr - 1];
                                    endTitles.Add(titleWithoutPart + "+\"1-" + ep + "\"");
                                    endTitles.Add(titleWithoutPart + "+\"1 ~ " + ep + "\"");
                                }
                            }

                            partFormed = true;
                        }

                        continue;
                    }

                    endTitles.Add(searchTitle + "+BATCH");
                    endTitles.Add(searchTitle + "+\"1-" + numberEpisodesSeason + "\"");
                    endTitles.Add(searchTitle + "+\"1 ~ " + numberEpisodesSeason + "\"");

                    if (searchCriteria.InteractiveSearch)
                    {
                        endTitles.Add(searchTitle);
                    }
                }

                // pageableRequests.Add(GetPagedRequests(searchTitle));
                // endTitles.Add(searchTitle);
            }

            /*
            if (searchCriteria.SeasonNumber > 1)
            {
                // --
                endTitles = endTitles.Where(t => t.ToLower().Contains("season")
                                            || !t.StartsWith("season", System.StringComparison.OrdinalIgnoreCase)).ToList();
            }
            */

            if (searchCriteria.SeasonNumber > 1)
            {
                var hasSeasonPattern = endTitles.Any(t =>
                    Regex.IsMatch(t.ToLower(), $@"\+s{searchCriteria.SeasonNumber:00}(?:\+|$)") ||
                    Regex.IsMatch(t.ToLower(), $@"\+s{searchCriteria.SeasonNumber}(?:\+|$)"));

                var hasSeasonKeyword = endTitles.Any(t =>
                    Regex.IsMatch(t.ToLower(), @"\+season\+\d+(?:\+|$)"));

                if (hasSeasonKeyword && !hasSeasonPattern)
                {
                    var newPatterns = new List<string>();
                    foreach (var title in endTitles.Where(t => Regex.IsMatch(t.ToLower(), @"\+season\+\d+(?:\+|$)")).ToList())
                    {
                        // Reemplazar "season X" por "sXX"
                        var newTitle = Regex.Replace(title.ToLower(), @"\+season\+\d+(?=\+|$)", $"+s{searchCriteria.SeasonNumber:00}");
                        newPatterns.Add(newTitle);
                    }

                    endTitles.AddRange(newPatterns);
                }
            }

            endTitles = endTitles.Select(title =>
            {
                var parts = title.Split(new[] { '+' }, System.StringSplitOptions.RemoveEmptyEntries);
                var distinctParts = parts.Distinct().ToList();
                return string.Join("+", distinctParts);
            }).ToList();

            var uniqueTitles = new List<string>();
            foreach (var title in endTitles)
            {
                var titleParts = title.Split(new[] { '+' }, System.StringSplitOptions.RemoveEmptyEntries)
                    .OrderBy(p => p)
                    .ToList();
                var titleKey = string.Join("+", titleParts);

                if (!uniqueTitles.Any(t =>
                {
                    var existingParts = t.Split(new[] { '+' }, System.StringSplitOptions.RemoveEmptyEntries)
                        .OrderBy(p => p)
                        .ToList();
                    var existingKey = string.Join("+", existingParts);
                    return existingKey == titleKey;
                }))
                {
                    uniqueTitles.Add(title);
                }
            }

            foreach (var finalTitle in uniqueTitles.Distinct())
            {
                pageableRequests.Add(GetPagedRequests(finalTitle));
                _logger.Info("Nyaa AnimeSeasonSearch: Final pattern: {0}", finalTitle);
            }

            // pageableRequests.Add(GetPagedRequests("Spy x Family"));
            _logger.Info("Nyaa AnimeSeasonSearch: Generated {0} search requests in {1} tiers", pageableRequests.GetAllTiers().Count(), pageableRequests.Tiers);
            return pageableRequests;
        }

        /*
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spyxfamily+BATCH
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spyxfamily+"1-12"
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spyxfamily+"1 ~ 12"
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spyxfamily
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy×family+BATCH
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy×family+"1-12"
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy×family+"1 ~ 12"
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy×family
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+x+family+part+2+BATCH
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+x+family+part+2
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+x+family+s2+BATCH
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+x+family+s2+"1-12"
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+x+family+s2+"1 ~ 12"
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+x+family+s2
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+x+family+season+2+BATCH
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+x+family+season+2+"1-12"
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+x+family+season+2+"1 ~ 12"
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+x+family+season+2
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+×+family+s2+BATCH
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+×+family+s2+"1-12"
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+×+family+s2+"1 ~ 12"
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+×+family+s2
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+x+family+BATCH
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+x+family+"1-12"
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+x+family+"1 ~ 12"
        [Info] NyaaRequestGenerator: Nyaa AnimeSeasonSearch: Final pattern: spy+x+family
        */

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
