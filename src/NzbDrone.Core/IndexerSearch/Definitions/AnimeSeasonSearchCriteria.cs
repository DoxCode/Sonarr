namespace NzbDrone.Core.IndexerSearch.Definitions
{
    public class AnimeSeasonSearchCriteria : SearchCriteriaBase
    {
        public int SeasonNumber { get; set; }

        /// <summary>
        /// Part number (1-based) if this season is split into multiple parts (e.g., Part 1, Part 2).
        /// 0 means this search covers the entire season or part detection is not applicable.
        /// </summary>
        public int SeasonPartNumber { get; set; }

        /// <summary>
        /// Total number of parts in this season, if applicable (e.g., 2 for a season split into Part 1 and Part 2).
        /// 0 means single part or not applicable.
        /// </summary>
        public int TotalSeasonParts { get; set; }

        public override string ToString()
        {
            if (SeasonPartNumber > 0 && TotalSeasonParts > 0)
            {
                return $"[{Series.Title} : S{SeasonNumber:00} Part {SeasonPartNumber}/{TotalSeasonParts}]";
            }

            return $"[{Series.Title} : S{SeasonNumber:00}]";
        }
    }
}
