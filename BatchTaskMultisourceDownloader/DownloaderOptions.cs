namespace BatchTaskMultisourceDownloader
{
    public class DownloaderOptions
    {
        public string LocalBasePath { get; set; }

        public double DownloadFromRemoteUriBias { get; set; }

        public double LoopIntervalMilliseconds { get; set; }

        public long PeerFragmentDownloadFactor { get; set; }
    }
}
