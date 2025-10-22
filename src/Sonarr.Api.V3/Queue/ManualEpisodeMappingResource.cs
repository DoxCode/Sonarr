using System.Collections.Generic;

namespace Sonarr.Api.V3.Queue
{
    public class ManualEpisodeMappingResource
    {
        public int QueueId { get; set; }
        public List<int> EpisodeIds { get; set; }
    }
}
