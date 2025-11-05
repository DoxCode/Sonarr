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
                        endTitles.Add(searchTitle + "+Season+" + searchCriteria.SeasonNumber);
                    }
                }

                // pageableRequests.Add(GetPagedRequests(searchTitle));
                // endTitles.Add(searchTitle);
            }

            if (partsEpisodes.Count > 2 && !partFormed)
            {
                var mids = partsEpisodes.Count - 2;

                foreach (var searchTitle in filteredTitles)
                {
                    // foreach a partsEpisodes ignorando el primero y ultimo.
                    for (var itr = 1; itr < partsEpisodes.Count; itr++)
                    {
                        var ep = partsEpisodes[itr] - partsEpisodes[itr - 1];
                        endTitles.Add(searchTitle + "+\"1-" + ep + "\"");
                        endTitles.Add(searchTitle + "+\"1 ~ " + ep + "\"");
                    }
                }
            }

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

            // Filtramos de endTitles dependiendo de la seasonNumber.
            // Por ejemplo Si la Season number es 2 y el titulo contiene "Season X", pero ese X no es 2, se elimina.
            // El ejemplo anterior se aplicará tambien para: SXX, donde XX no es la seasonNumber.
            // Y también se aplicará para números romanos: Por ejemplo: Titulo Serie II, daremos por hecho que ese II es la seasonNumber 2
            // En el caso anterior con los numeros romanos, siempre habra un espacio antes del numero romano y despues, tambien puede ser un "." o una "-"

            // First, filter out titles that explicitly reference a different season number
            // Cases handled:
            //  - "+season+X" where X != SeasonNumber
            //  - tokens like "+sXX" (or sX) where number != SeasonNumber
            //  - standalone roman numerals (I, II, III, IV, V, ...) used as season hints that != SeasonNumber
            endTitles = endTitles
                .Where(t => MatchesSeasonTokens(t, searchCriteria.SeasonNumber))
                .Select(title =>
                {
                    // Then, remove duplicate parts to avoid redundant query terms
                    var parts = title.Split(new[] { '+' }, System.StringSplitOptions.RemoveEmptyEntries);
                    var distinctParts = parts.Distinct().ToList();
                    return string.Join("+", distinctParts);
                })
                .ToList();

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

        private static bool MatchesSeasonTokens(string title, int seasonNumber)
        {
            if (seasonNumber <= 0 || string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            var t = title.ToLowerInvariant();
            var tokens = t.Split(new[] { '+' }, System.StringSplitOptions.RemoveEmptyEntries);

            // Check pattern: +season+<number>
            for (var i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] == "season" && i + 1 < tokens.Length)
                {
                    if (int.TryParse(tokens[i + 1], out var seasonToken))
                    {
                        if (seasonToken != seasonNumber)
                        {
                            return false;
                        }
                    }
                }
            }

            // Check tokens like s2, s02
            var sMatch = Regex.Matches(t, @"(?<=\+)s0?\d{1,2}(?=\+|$)");
            foreach (Match m in sMatch)
            {
                if (m.Success)
                {
                    var numPart = m.Value.TrimStart('s');
                    if (int.TryParse(numPart, out var sNum))
                    {
                        if (sNum != seasonNumber)
                        {
                            return false;
                        }
                    }
                }
            }

            // Check standalone roman numerals as tokens (I, II, III, IV, V, ...)
            foreach (var tok in tokens)
            {
                if (IsLikelyRomanToken(tok))
                {
                    var romanVal = RomanToInt(tok);
                    if (romanVal > 0 && romanVal != seasonNumber)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsLikelyRomanToken(string token)
        {
            // Limit to common roman numerals range and avoid mistaking words; token must be only roman letters
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (!Regex.IsMatch(token, @"^[mdclxvi]+$"))
            {
                return false;
            }

            // Keep it short to avoid over-matching long words; typical season numerals are <= xx (20)
            return token.Length <= 4; // e.g., i, ii, iii, iv, v, vi, vii, viii, ix, x, xi, xii, xiii, xiv, xv, xvi, xvii, xviii, xix, xx
        }

        private static int RomanToInt(string roman)
        {
            if (string.IsNullOrWhiteSpace(roman))
            {
                return 0;
            }

            var s = roman.ToUpperInvariant();
            var total = 0;
            var prev = 0;

            foreach (var c in s)
            {
                var val = c switch
                {
                    'I' => 1,
                    'V' => 5,
                    'X' => 10,
                    'L' => 50,
                    'C' => 100,
                    'D' => 500,
                    'M' => 1000,
                    _ => 0
                };

                if (val == 0)
                {
                    return 0;
                }

                if (val > prev)
                {
                    total += val - (2 * prev); // adjust previous addition
                }
                else
                {
                    total += val;
                }

                prev = val;
            }

            return total;
        }
    }
}
