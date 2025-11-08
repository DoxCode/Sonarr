using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Tv;
using Sonarr.Http;
using BadRequestException = Sonarr.Http.REST.BadRequestException;

namespace Sonarr.Api.V3.FileSystem
{
    [V3ApiController]
    public class FileSystemController : Controller
    {
        private readonly IFileSystemLookupService _fileSystemLookupService;
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskScanService _diskScanService;
        private readonly ISeriesService _seriesService;

        public FileSystemController(IFileSystemLookupService fileSystemLookupService,
                                IDiskProvider diskProvider,
                                IDiskScanService diskScanService,
                                ISeriesService seriesService)
        {
            _fileSystemLookupService = fileSystemLookupService;
            _diskProvider = diskProvider;
            _diskScanService = diskScanService;
            _seriesService = seriesService;
        }

        [HttpGet]
        [Produces("application/json")]
        public IActionResult GetContents(string path, bool includeFiles = false, bool allowFoldersWithoutTrailingSlashes = false)
        {
            return Ok(_fileSystemLookupService.LookupContents(path, includeFiles, allowFoldersWithoutTrailingSlashes));
        }

        [HttpGet("type")]
        [Produces("application/json")]
        public object GetEntityType(string path)
        {
            if (_diskProvider.FileExists(path))
            {
                return new { type = "file" };
            }

            // Return folder even if it doesn't exist on disk to avoid leaking anything from the UI about the underlying system
            return new { type = "folder" };
        }

        [HttpGet("mediafiles")]
        [Produces("application/json")]
        public object GetMediaFiles(string path)
        {
            if (!_diskProvider.FolderExists(path))
            {
                return Array.Empty<string>();
            }

            return _diskScanService.GetVideoFiles(path).Select(f => new
            {
                Path = f,
                RelativePath = path.GetRelativePath(f),
                Name = Path.GetFileName(f)
            });
        }

        [HttpGet("series/{seriesId}")]
        [Produces("application/json")]
        public List<FileSystemEntryResource> BrowseSeriesFolder(int seriesId, [FromQuery] string path = null)
        {
            var series = _seriesService.GetSeries(seriesId);

            if (series == null)
            {
                throw new NzbDroneClientException(global::System.Net.HttpStatusCode.NotFound, "Series not found");
            }

            var basePath = series.Path;

            // If no path specified, use the series root
            var searchPath = string.IsNullOrWhiteSpace(path) ? basePath : path;

            // Validate that the path is within the series directory
            var fullSearchPath = global::System.IO.Path.GetFullPath(searchPath);
            var fullBasePath = global::System.IO.Path.GetFullPath(basePath);

            if (!fullSearchPath.StartsWith(fullBasePath, global::System.StringComparison.OrdinalIgnoreCase))
            {
                throw new BadRequestException("Path must be within the series directory");
            }

            // Check if directory exists
            if (!_diskProvider.FolderExists(fullSearchPath))
            {
                throw new BadRequestException("Directory does not exist");
            }

            var resources = new List<FileSystemEntryResource>();

            // Add parent directory navigation if not at series root
            if (!fullSearchPath.Equals(fullBasePath, global::System.StringComparison.OrdinalIgnoreCase))
            {
                var parentPath = global::System.IO.Path.GetDirectoryName(fullSearchPath);
                if (!string.IsNullOrEmpty(parentPath) && parentPath.StartsWith(fullBasePath, global::System.StringComparison.OrdinalIgnoreCase))
                {
                    resources.Add(new FileSystemEntryResource
                    {
                        Path = parentPath,
                        Name = "..",
                        IsDirectory = true,
                        IsParent = true
                    });
                }
            }

            try
            {
                // Add subdirectories
                var dirInfo = new DirectoryInfo(fullSearchPath);
                var directories = dirInfo.GetDirectories().OrderBy(d => d.Name);

                foreach (var dir in directories)
                {
                    resources.Add(new FileSystemEntryResource
                    {
                        Path = dir.FullName,
                        Name = dir.Name,
                        IsDirectory = true,
                        IsParent = false
                    });
                }

                // Add files
                var files = dirInfo.GetFiles().OrderBy(f => f.Name);

                foreach (var file in files)
                {
                    resources.Add(new FileSystemEntryResource
                    {
                        Path = file.FullName,
                        Name = file.Name,
                        IsDirectory = false,
                        IsParent = false,
                        Size = file.Length
                    });
                }
            }
            catch (global::System.UnauthorizedAccessException)
            {
                throw new BadRequestException("Access denied to directory");
            }

            return resources;
        }
    }
}
