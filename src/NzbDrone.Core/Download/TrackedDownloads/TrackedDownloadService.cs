using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download.Aggregation;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public interface ITrackedDownloadService
    {
        TrackedDownload Find(string downloadId);
        void StopTracking(string downloadId);
        void StopTracking(List<string> downloadIds);
        TrackedDownload TrackDownload(DownloadClientDefinition downloadClient, DownloadClientItem downloadItem);
        List<TrackedDownload> GetTrackedDownloads();
        void UpdateTrackable(List<TrackedDownload> trackedDownloads);
    }

    public class TrackedDownloadService : ITrackedDownloadService,
                                          IHandle<EpisodeInfoRefreshedEvent>,
                                          IHandle<SeriesAddedEvent>,
                                          IHandle<SeriesDeletedEvent>
    {
        private readonly IParsingService _parsingService;
        private readonly IHistoryService _historyService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IEpisodeService _episodeService;
        private readonly ISeriesService _seriesService;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly IRemoteEpisodeAggregationService _aggregationService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly Logger _logger;
        private readonly ICached<TrackedDownload> _cache;

        public TrackedDownloadService(IParsingService parsingService,
                                      ICacheManager cacheManager,
                                      IHistoryService historyService,
                                      ICustomFormatCalculationService formatCalculator,
                                      IEventAggregator eventAggregator,
                                      IDownloadHistoryService downloadHistoryService,
                                      IRemoteEpisodeAggregationService aggregationService,
                                      IEpisodeService episodeService,
                                      ISeriesService seriesService,
                                      Logger logger)
        {
            _parsingService = parsingService;
            _historyService = historyService;
            _formatCalculator = formatCalculator;
            _eventAggregator = eventAggregator;
            _downloadHistoryService = downloadHistoryService;
            _aggregationService = aggregationService;
            _episodeService = episodeService;
            _seriesService = seriesService;
            _cache = cacheManager.GetCache<TrackedDownload>(GetType());
            _logger = logger;
        }

        public TrackedDownload Find(string downloadId)
        {
            return _cache.Find(downloadId);
        }

        public void StopTracking(string downloadId)
        {
            var trackedDownload = _cache.Find(downloadId);

            _cache.Remove(downloadId);
            _eventAggregator.PublishEvent(new TrackedDownloadsRemovedEvent(new List<TrackedDownload> { trackedDownload }));
        }

        public void StopTracking(List<string> downloadIds)
        {
            var trackedDownloads = new List<TrackedDownload>();

            foreach (var downloadId in downloadIds)
            {
                var trackedDownload = _cache.Find(downloadId);
                _cache.Remove(downloadId);
                trackedDownloads.Add(trackedDownload);
            }

            _eventAggregator.PublishEvent(new TrackedDownloadsRemovedEvent(trackedDownloads));
        }

        public TrackedDownload TrackDownload(DownloadClientDefinition downloadClient, DownloadClientItem downloadItem)
        {
            var existingItem = Find(downloadItem.DownloadId);

            if (existingItem != null && existingItem.State != TrackedDownloadState.Downloading)
            {
                checkAnChangeIfPart(existingItem, existingItem.RemoteEpisode?.ParsedEpisodeInfo, downloadItem);
                LogItemChange(existingItem, existingItem.DownloadItem, downloadItem);

                existingItem.DownloadItem = downloadItem;
                existingItem.IsTrackable = true;
                return existingItem;
            }

            var trackedDownload = new TrackedDownload
            {
                DownloadClient = downloadClient.Id,
                DownloadItem = downloadItem,
                Protocol = downloadClient.Protocol,
                IsTrackable = true,
                HasNotifiedManualInteractionRequired = existingItem?.HasNotifiedManualInteractionRequired ?? false
            };

            try
            {
                var historyItems = _historyService.FindByDownloadId(downloadItem.DownloadId)
                    .OrderByDescending(h => h.Date)
                    .ToList();

                var parsedEpisodeInfo = Parser.Parser.ParseTitle(trackedDownload.DownloadItem.Title);

                if (parsedEpisodeInfo != null)
                {
                    trackedDownload.RemoteEpisode = _parsingService.Map(parsedEpisodeInfo, 0, 0, null);
                }

                if (checkAnChangeIfPart(trackedDownload, parsedEpisodeInfo, downloadItem))
                {
                    return TrackDownload(downloadClient, downloadItem);
                }

                var downloadHistory = _downloadHistoryService.GetLatestDownloadHistoryItem(downloadItem.DownloadId);

                if (downloadHistory != null)
                {
                    var state = GetStateFromHistory(downloadHistory.EventType);
                    trackedDownload.State = state;
                }

                if (historyItems.Any())
                {
                    var firstHistoryItem = historyItems.First();
                    var grabbedEvent = historyItems.FirstOrDefault(v => v.EventType == EpisodeHistoryEventType.Grabbed);

                    trackedDownload.Indexer = grabbedEvent?.Data?.GetValueOrDefault("indexer");
                    trackedDownload.Added = grabbedEvent?.Date;

                    if (parsedEpisodeInfo == null ||
                        trackedDownload.RemoteEpisode?.Series == null ||
                        trackedDownload.RemoteEpisode.Episodes.Empty())
                    {
                        // Try parsing the original source title and if that fails, try parsing it as a special
                        // TODO: Pass the TVDB ID and TVRage IDs in as well so we have a better chance for finding the item
                        parsedEpisodeInfo = Parser.Parser.ParseTitle(firstHistoryItem.SourceTitle) ??
                                            _parsingService.ParseSpecialEpisodeTitle(parsedEpisodeInfo, firstHistoryItem.SourceTitle, 0, 0, null);

                        if (parsedEpisodeInfo != null)
                        {
                            trackedDownload.RemoteEpisode = _parsingService.Map(parsedEpisodeInfo,
                                firstHistoryItem.SeriesId,
                                historyItems.Where(v => v.EventType == EpisodeHistoryEventType.Grabbed)
                                    .Select(h => h.EpisodeId).Distinct());
                        }
                    }

                    if (trackedDownload.RemoteEpisode != null)
                    {
                        trackedDownload.RemoteEpisode.Release ??= new ReleaseInfo();
                        trackedDownload.RemoteEpisode.Release.Indexer = trackedDownload.Indexer;
                        trackedDownload.RemoteEpisode.Release.Title = trackedDownload.RemoteEpisode.ParsedEpisodeInfo?.ReleaseTitle;

                        if (Enum.TryParse(grabbedEvent?.Data?.GetValueOrDefault("indexerFlags"), true, out IndexerFlags flags))
                        {
                            trackedDownload.RemoteEpisode.Release.IndexerFlags = flags;
                        }

                        if (downloadHistory != null)
                        {
                            trackedDownload.RemoteEpisode.Release.IndexerId = downloadHistory.IndexerId;
                        }
                    }
                }

                if (trackedDownload.RemoteEpisode != null)
                {
                    _aggregationService.Augment(trackedDownload.RemoteEpisode);

                    // Calculate custom formats
                    trackedDownload.RemoteEpisode.CustomFormats = _formatCalculator.ParseCustomFormat(trackedDownload.RemoteEpisode, downloadItem.TotalSize);
                }

                // Track it so it can be displayed in the queue even though we can't determine which series it is for
                if (trackedDownload.RemoteEpisode == null)
                {
                    _logger.Trace("No Episode found for download '{0}'", trackedDownload.DownloadItem.Title);
                }
            }
            catch (MultipleSeriesFoundException e)
            {
                _logger.Debug(e, "Found multiple series for " + downloadItem.Title);

                trackedDownload.Warn("Unable to import automatically, found multiple series: {0}", string.Join(", ", e.Series));
            }
            catch (Exception e)
            {
                _logger.Debug(e, "Failed to find episode for " + downloadItem.Title);

                trackedDownload.Warn("Unable to parse episodes from title");
            }

            LogItemChange(trackedDownload, existingItem?.DownloadItem, trackedDownload.DownloadItem);

            if (trackedDownload.DownloadItem != null)
            {
                _cache.Set(trackedDownload.DownloadItem.DownloadId, trackedDownload);
            }

            return trackedDownload;
        }

        private bool checkAnChangeIfPart(TrackedDownload trackedDownload, ParsedEpisodeInfo parsedEpisodeInfo, DownloadClientItem downloadItem)
        {
            var partNumber = Parser.Parser.ParsePartNumber(trackedDownload.DownloadItem.Title);

            if (partNumber.HasValue)
            {
                var partNumberValue = partNumber.Value;
                var season = -1;
                var seriesId = -1;

                if (trackedDownload.RemoteEpisode.Episodes.Count > 0)
                {
                    season = trackedDownload.RemoteEpisode.Episodes[0].SeasonNumber;
                    seriesId = trackedDownload.RemoteEpisode.Episodes[0].SeriesId;
                }

                if (seriesId != -1 && season != -1 && partNumberValue > 1)
                {
                    var fullEpisodeSeason = _episodeService.GetEpisodesBySeason(seriesId, season);
                    var midseasonEpisodes = fullEpisodeSeason.Where(episode => episode.FinaleType == "midseason").ToList();
                    midseasonEpisodes.Sort((a, b) => a.EpisodeNumber.CompareTo(b.EpisodeNumber));

                    if (partNumberValue - 1 <= midseasonEpisodes.Count)
                    {
                        var offset = midseasonEpisodes[partNumberValue - 2].EpisodeNumber;

                        var originalAbsolute = (parsedEpisodeInfo.AbsoluteEpisodeNumbers ?? Array.Empty<int>()).ToArray();
                        if (!parsedEpisodeInfo.AbsoluteEpisodeNumbers.Any(n => n + offset > fullEpisodeSeason.Count))
                        {
                            parsedEpisodeInfo.AbsoluteEpisodeNumbers = parsedEpisodeInfo.AbsoluteEpisodeNumbers.Select(n => n + offset).ToArray();

                            var fileName = downloadItem.OutputPath.FileName;

                            var ext = Path.GetExtension(fileName);

                            var partPattern = new System.Text.RegularExpressions.Regex($@"(?i)\bPart[ _.\-]*0?{partNumberValue}\b");
                            var previousPath = downloadItem.OutputPath.FullPath;

                            if (ext == null || ext.Empty())
                            {
                                var name = Path.GetFileNameWithoutExtension(fileName);
                                name = partPattern.Replace(name, string.Empty).Trim();

                                var origMin = originalAbsolute.Min();
                                var origMax = originalAbsolute.Max();
                                var newMin = parsedEpisodeInfo.AbsoluteEpisodeNumbers.Min();
                                var newMax = parsedEpisodeInfo.AbsoluteEpisodeNumbers.Max();

                                var rangeDash = new System.Text.RegularExpressions.Regex($@"(?<=^|[^0-9])0?{origMin}\s*-\s*0?{origMax}(?=$|[^0-9])");
                                var rangeTilde = new System.Text.RegularExpressions.Regex($@"(?<=^|[^0-9])0?{origMin}\s*~\s*0?{origMax}(?=$|[^0-9])");

                                name = rangeDash.Replace(name, $"{newMin:00}-{newMax:00}");
                                name = rangeTilde.Replace(name, $"{newMin:00} ~ {newMax:00}");
                                name = name.Replace("  ", " ");

                                var previousName = downloadItem.OutputPath.FileName;

                                var nName = downloadItem.OutputPath.FullPath.Replace(previousName, name);
                                downloadItem.OutputPath = new OsPath(nName);

                                downloadItem.DownloadId = downloadItem.DownloadId.Replace(previousName, name);
                                downloadItem.Title = downloadItem.Title.Replace(previousName, name);

                                try
                                {
                                    Directory.Move(previousPath, nName);
                                }
                                catch (Exception ex)
                                {
                                    _logger.Warn(ex, "Failed modifing name folder {0}", nName);
                                }

                                if (System.IO.Directory.Exists(nName))
                                {
                                    foreach (var filePath in System.IO.Directory.EnumerateFiles(nName, "*", SearchOption.TopDirectoryOnly))
                                    {
                                        var childName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                                        var childExt = System.IO.Path.GetExtension(filePath);

                                        // Eliminar "Part X/XX"
                                        var newName = partPattern.Replace(childName, string.Empty).Trim();

                                        // Reemplazar episodios n/0n por n+offset en formato 2 dígitos
                                        /*foreach (var n in originalAbsolute)
                                        {
                                            var newVal = n + offset;
                                            var numPattern = new System.Text.RegularExpressions.Regex($@"(?<=^|[^0-9])0?{n}(?=$|[^0-9])");
                                            newName = numPattern.Replace(newName, newVal.ToString("00"));
                                        }*/

                                        newName = MapEpisodesInName(newName, offset, originalAbsolute, parsedEpisodeInfo.SeriesTitleInfo.AllTitles);

                                        newName = System.Text.RegularExpressions.Regex.Replace(newName, @"\s{2,}", " ").Trim();
                                        var modifiedFileName = newName + childExt;

                                        var newFullPath = System.IO.Path.Combine(nName, modifiedFileName);

                                        if (!string.Equals(filePath, newFullPath, StringComparison.OrdinalIgnoreCase))
                                        {
                                            try
                                            {
                                                if (!System.IO.File.Exists(filePath))
                                                {
                                                    _logger.Warn("Original file not found at '{0}'", filePath);
                                                }
                                                else if (System.IO.File.Exists(newFullPath))
                                                {
                                                    _logger.Warn("Target file already exists at '{0}', skipping rename.", newFullPath);
                                                }
                                                else
                                                {
                                                    System.IO.File.Move(filePath, newFullPath);
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                _logger.Warn(ex, "Failed to rename file from '{0}' to '{1}'", filePath, newFullPath);
                                            }
                                        }
                                    }
                                }

                                return true;
                            }
                            else if (trackedDownload.RemoteEpisode.Episodes.Count == 1)
                            {
                                var name = Path.GetFileNameWithoutExtension(fileName);

                                name = partPattern.Replace(name, string.Empty).Trim();
                                name = MapEpisodesInName(name, offset, originalAbsolute, parsedEpisodeInfo.SeriesTitleInfo.AllTitles);
                                name = System.Text.RegularExpressions.Regex.Replace(name, @"\s{2,}", " ").Trim();

                                var modifiedFileName = name + ext;
                                var previousName = downloadItem.OutputPath.FileName;

                                var newName = downloadItem.OutputPath.FullPath.Replace(previousName, modifiedFileName);
                                downloadItem.OutputPath = new OsPath(newName);

                                downloadItem.DownloadId = downloadItem.DownloadId.Replace(previousName, modifiedFileName);
                                downloadItem.Title = downloadItem.Title.Replace(previousName, modifiedFileName);

                                // Renombrar físicamente el fichero en disco
                                var previousDir = Path.GetDirectoryName(previousPath);
                                var newFullPath = Path.Combine(previousDir ?? string.Empty, modifiedFileName);

                                if (!string.Equals(previousPath, newFullPath, StringComparison.OrdinalIgnoreCase))
                                {
                                    try
                                    {
                                        if (!File.Exists(previousPath))
                                        {
                                            _logger.Warn("Original file not found at '{0}'", previousPath);
                                        }
                                        else if (File.Exists(newFullPath))
                                        {
                                            _logger.Warn("Target file already exists at '{0}', skipping rename.", newFullPath);
                                        }
                                        else
                                        {
                                            File.Move(previousPath, newFullPath);
                                            return true;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.Warn(ex, "Failed to rename file from '{0}' to '{1}'", previousPath, newFullPath);
                                    }
                                }

                                return false;
                            }

                            // trackedDownload.RemoteEpisode = _parsingService.Map(parsedEpisodeInfo, 0, 0, null);
                            return false;
                        }
                    }
                }
            }

            return false;
        }

        private static string MapEpisodesInName(string name, int offset, int[] originals, string[] seriesTitles)
        {
            if (string.IsNullOrWhiteSpace(name) || originals == null || originals.Length == 0)
            {
                return name;
            }

            var scan = name;

            // Limpieza solo para detección (no se aplica a 'name')
            scan = System.Text.RegularExpressions.Regex.Replace(scan, @"(?i)(?:^|[ ._\-/])Season\s*\d{1,2}(?=$|[ ._\-/])", " ");
            scan = System.Text.RegularExpressions.Regex.Replace(scan, @"(?i)S\s*0?\d{1,2}(?=\s*[Ee]\s*\d{1,2})", " ");
            scan = System.Text.RegularExpressions.Regex.Replace(scan, @"(?i)(?:^|[ ._\-/])S\s*0?\d{1,2}(?=$|[ ._\-/])", " ");
            foreach (var t in seriesTitles ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(t))
                {
                    var esc = System.Text.RegularExpressions.Regex.Escape(t.Trim());
                    scan = System.Text.RegularExpressions.Regex.Replace(scan, esc, " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
            }

            scan = System.Text.RegularExpressions.Regex.Replace(scan, @"\s{2,}", " ").Trim();

            var delim = @"[ \.\-_/]";
            string Pattern(bool twoDigit) => twoDigit
                ? $@"(?ix)(?<=^|{delim})(?'prefix'[Ee]?\s*)(?'ep'(?:0[1-9]|[1-9]\d))(?'suffix'v\d+)?(?=$|{delim})|(?ix)(?<=[Ee])\s*(?'ep'(?:0[1-9]|[1-9]\d))(?'suffix'v\d+)?(?=$|[^0-9])"
                : $@"(?ix)(?<=^|{delim})(?'prefix'[Ee]?\s*)(?'ep'(?:[1-9]))(?'suffix'v\d+)?(?=$|{delim})|(?ix)(?<=[Ee])\s*(?'ep'(?:[1-9]))(?'suffix'v\d+)?(?=$|[^0-9])";

            System.Collections.Generic.List<int> Collect(string s, string pattern)
            {
                var vals = new System.Collections.Generic.List<int>();
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(s, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.IgnorePatternWhitespace))
                {
                    if (m.Success && int.TryParse(m.Groups["ep"].Value, out var v))
                    {
                        vals.Add(v);
                    }
                }

                return vals;
            }

            var found = Collect(scan, Pattern(true));
            if (found.Count != found.Distinct().Count())
            {
                scan = System.Text.RegularExpressions.Regex.Replace(scan, @"\[[^\]]*\]", " ");
                scan = System.Text.RegularExpressions.Regex.Replace(scan, @"\s{2,}", " ").Trim();
                found = Collect(scan, Pattern(true));
            }

            if (found.Count == 0)
            {
                found = Collect(scan, Pattern(false));
                if (found.Count != found.Distinct().Count())
                {
                    scan = System.Text.RegularExpressions.Regex.Replace(scan, @"\[[^\]]*\]", " ");
                    scan = System.Text.RegularExpressions.Regex.Replace(scan, @"\s{2,}", " ").Trim();
                    found = Collect(scan, Pattern(false));
                }
            }

            var toUpdate = new HashSet<int>(found.Intersect(originals));
            if (toUpdate.Count == 0)
            {
                return name;
            }

            string Eval(System.Text.RegularExpressions.Match m)
            {
                var epStr = m.Groups["ep"].Value;
                if (!int.TryParse(epStr, out var ep) || !toUpdate.Contains(ep))
                {
                    return m.Value;
                }

                var prefix = m.Groups["prefix"].Success ? m.Groups["prefix"].Value : string.Empty;
                var suffix = m.Groups["suffix"].Success ? m.Groups["suffix"].Value : string.Empty;
                return $"{prefix}{ep + offset:00}{suffix}";
            }

            var replaced = System.Text.RegularExpressions.Regex.Replace(name, Pattern(true), new System.Text.RegularExpressions.MatchEvaluator(Eval), System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.IgnorePatternWhitespace);

            if (replaced == name && toUpdate.Any(v => v <= 9))
            {
                replaced = System.Text.RegularExpressions.Regex.Replace(replaced, Pattern(false), new System.Text.RegularExpressions.MatchEvaluator(Eval), System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.IgnorePatternWhitespace);
            }

            return System.Text.RegularExpressions.Regex.Replace(replaced, @"\s{2,}", " ").Trim();
        }

        public List<TrackedDownload> GetTrackedDownloads()
        {
            return _cache.Values.ToList();
        }

        public void UpdateTrackable(List<TrackedDownload> trackedDownloads)
        {
            var untrackable = GetTrackedDownloads().ExceptBy(t => t.DownloadItem.DownloadId, trackedDownloads, t => t.DownloadItem.DownloadId, StringComparer.CurrentCulture).ToList();

            foreach (var trackedDownload in untrackable)
            {
                trackedDownload.IsTrackable = false;
            }
        }

        private void LogItemChange(TrackedDownload trackedDownload, DownloadClientItem existingItem, DownloadClientItem downloadItem)
        {
            if (existingItem == null ||
                existingItem.Status != downloadItem.Status ||
                existingItem.CanBeRemoved != downloadItem.CanBeRemoved ||
                existingItem.CanMoveFiles != downloadItem.CanMoveFiles)
            {
                _logger.Debug("Tracking '{0}:{1}': ClientState={2}{3} SonarrStage={4} Episode='{5}' OutputPath={6}.",
                    downloadItem.DownloadClientInfo.Name,
                    downloadItem.Title,
                    downloadItem.Status,
                    downloadItem.CanBeRemoved ? "" : downloadItem.CanMoveFiles ? " (busy)" : " (readonly)",
                    trackedDownload.State,
                    trackedDownload.RemoteEpisode?.ParsedEpisodeInfo,
                    downloadItem.OutputPath);
            }
        }

        private void UpdateCachedItem(TrackedDownload trackedDownload)
        {
            var parsedEpisodeInfo = Parser.Parser.ParseTitle(trackedDownload.DownloadItem.Title);

            trackedDownload.RemoteEpisode = parsedEpisodeInfo == null ? null : _parsingService.Map(parsedEpisodeInfo, 0, 0, null);

            _aggregationService.Augment(trackedDownload.RemoteEpisode);
        }

        private static TrackedDownloadState GetStateFromHistory(DownloadHistoryEventType eventType)
        {
            switch (eventType)
            {
                case DownloadHistoryEventType.DownloadImported:
                    return TrackedDownloadState.Imported;
                case DownloadHistoryEventType.DownloadFailed:
                    return TrackedDownloadState.Failed;
                case DownloadHistoryEventType.DownloadIgnored:
                    return TrackedDownloadState.Ignored;
                default:
                    return TrackedDownloadState.Downloading;
            }
        }

        public void Handle(EpisodeInfoRefreshedEvent message)
        {
            var needsToUpdate = false;

            foreach (var episode in message.Removed)
            {
                var cachedItems = _cache.Values.Where(t =>
                                            t.RemoteEpisode?.Episodes != null &&
                                            t.RemoteEpisode.Episodes.Any(e => e.Id == episode.Id))
                                        .ToList();

                if (cachedItems.Any())
                {
                    needsToUpdate = true;
                }

                cachedItems.ForEach(UpdateCachedItem);
            }

            if (needsToUpdate)
            {
                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }

        public void Handle(SeriesAddedEvent message)
        {
            var cachedItems = _cache.Values
                .Where(t =>
                    t.RemoteEpisode?.Series == null ||
                    message.Series?.TvdbId == t.RemoteEpisode.Series.TvdbId)
                .ToList();

            if (cachedItems.Any())
            {
                cachedItems.ForEach(UpdateCachedItem);

                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }

        public void Handle(SeriesDeletedEvent message)
        {
            var cachedItems = _cache.Values
                .Where(t =>
                    t.RemoteEpisode?.Series != null &&
                    message.Series.Any(s => s.Id == t.RemoteEpisode.Series.Id || s.TvdbId == t.RemoteEpisode.Series.TvdbId))
                .ToList();

            if (cachedItems.Any())
            {
                cachedItems.ForEach(UpdateCachedItem);

                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }
    }
}
