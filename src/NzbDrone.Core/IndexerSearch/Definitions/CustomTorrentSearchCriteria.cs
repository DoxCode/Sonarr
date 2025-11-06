using System.Collections.Generic;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    public class CustomTorrentSearchCriteria : SearchCriteriaBase
    {
        public string CustomSearchTerm { get; set; }

        public CustomTorrentSearchCriteria()
        {
            Series = null;
            SceneTitles = new List<string>();
            Episodes = new List<Episode>();
        }

        public override string ToString()
        {
            return $"[CustomTorrent: {CustomSearchTerm}]";
        }
    }
}
