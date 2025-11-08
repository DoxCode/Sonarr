namespace Sonarr.Api.V3.FileSystem
{
    public class FileSystemEntryResource
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public bool IsDirectory { get; set; }
        public bool IsParent { get; set; }
        public long? Size { get; set; }
    }
}
