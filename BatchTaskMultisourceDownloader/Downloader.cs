using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace BatchTaskMultisourceDownloader
{
    public class Downloader
    {
        private readonly ILogger<Downloader> logger;
        private readonly DownloaderOptions options;
        private readonly ManifestInfo manifest;
        private readonly IKeyValueStore store;
        private readonly Func<IEnumerable<string>> memberListProvider;
        private readonly Func<string, Channel> channelFactory;
        private readonly Func<string, long, long, Task<byte[]>> segmentDownloader;
        private readonly DownloaderServiceImpl downloaderServiceImpl;

        public Downloader(
            ILogger<Downloader> logger,
            DownloaderOptions options,
            ManifestInfo manifest,
            IKeyValueStore store,
            Func<IEnumerable<string>> memberListProvider,
            Func<string, Channel> channelFactory,
            Func<string, long, long, Task<byte[]>> segmentDownloader)
        {
            this.logger = logger;
            this.options = options;
            this.manifest = manifest;
            this.store = store;
            this.memberListProvider = memberListProvider;
            this.channelFactory = channelFactory;
            this.segmentDownloader = segmentDownloader;

            downloaderServiceImpl = new DownloaderServiceImpl(store);
        }

        public DownloaderService.DownloaderServiceBase DownloaderService => downloaderServiceImpl;

        public async Task InvokeAsync(CancellationToken cancellationToken = default)
        {
            var knownSegments = new HashSet<long>();
            var unknownFragments = new HashSet<long>(manifest.FragmentMap.Keys);
            var unknownFiles = new HashSet<long>(manifest.FileMap.Keys);

            var stopwatch = new Stopwatch();
            var random = new Random();
            while (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Restart();
                if (random.NextDouble() > options.DownloadFromRemoteUriBias)
                {
                    logger.LogDebug("Download segment from remote Uri");
                    var unknownSegments = manifest.SegmentMap.Values
                        .Where(s => !knownSegments.Contains(s.Key));
                    if (unknownSegments.Any())
                    {
                        var segment = unknownSegments.ReservoirSample(random, 1).Single();
                        var file = manifest.FileMap[segment.FileKey];
                        logger.LogDebug(
                            "Choose segment {0} from file({1}, {2})",
                            segment.Key,
                            file.Key,
                            segment.OffsetInFile);

                        var segmentValue = await DownloadSegmentAsync(file, segment);
                        logger.LogDebug(
                            "Downloaded segment {0} offset={1} length={2}",
                            segment.Key, segment.OffsetInFile, segmentValue.Length);
                        var fragmentKeyValues = segment.FragmentKeys.Zip(
                            SplitByLength(segmentValue, (int)manifest.FragmentSize),
                            Tuple.Create);
                        foreach (var p in fragmentKeyValues)
                        {
                            var fragment = manifest.FragmentMap[p.Item1];
                            // Save fragment value to KV store.
                            store.Put(
                                p.Item1,
                                p.Item2.ToArray());
                            unknownFragments.Remove(p.Item1);
                            logger.LogDebug("Fragment {0} saved ({1} bytes)", fragment.Key, p.Item2.Count);
                        }
                        knownSegments.Add(segment.Key);
                        logger.LogDebug("All fragments of segment {0} known", segment.Key);

                        if (IsFileKnownFromItsSegments(file, knownSegments))
                        {
                            logger.LogInformation("All segments of file {0} known", file.Key);
                            await WriteFileToLocalPath(file);
                            unknownFiles.Remove(file.Key);
                        }
                    }
                }
                else
                {
                    logger.LogDebug("Download fragments from peer");
                    var wantFragmentCountFromPeer = (int)(manifest.SegmentSize / manifest.FragmentSize * options.PeerFragmentDownloadFactor);
                    var wantFragmentKeys = unknownFragments.ReservoirSample(random, wantFragmentCountFromPeer);
                    var pullRequest = new PullRequest();
                    pullRequest.FragmentKeys.AddRange(wantFragmentKeys);
                    logger.LogDebug("Pull fragments [{0}]", string.Join(",", pullRequest.FragmentKeys));

                    var peerAddress = memberListProvider.Invoke().ReservoirSample(random, 1).Single();
                    var client = new DownloaderService.DownloaderServiceClient(channelFactory.Invoke(peerAddress));
                    using (var pullCall = client.Pull(pullRequest))
                    {
                        logger.LogDebug("Pulling from peer {0}", peerAddress);
                        while (await pullCall.ResponseStream.MoveNext())
                        {
                            var pullResponse = pullCall.ResponseStream.Current;
                            store.Put(
                                pullResponse.FragmentKey,
                                pullResponse.FragmentValue.ToByteArray());
                            unknownFragments.Remove(pullResponse.FragmentKey);
                            logger.LogDebug("Fragment {0} saved", pullResponse.FragmentKey);

                            var fragment = manifest.FragmentMap[pullResponse.FragmentKey];
                            var segment = manifest.SegmentMap[fragment.SegmentKey];
                            if (IsSegmentKnownFromItsFragments(segment, unknownFragments))
                            {
                                logger.LogDebug("All fragments of segment {0} known", segment.Key);
                                knownSegments.Add(segment.Key);
                                var file = manifest.FileMap[segment.FileKey];
                                if (IsFileKnownFromItsSegments(file, knownSegments))
                                {
                                    logger.LogInformation("All segments of file {0} known", file.Key);
                                    await WriteFileToLocalPath(file);
                                    unknownFiles.Remove(file.Key);
                                }
                            }
                        }
                    }
                }

                stopwatch.Stop();
                if (unknownFiles.Count == 0) break;

                var sleepMilliseconds = options.LoopIntervalMilliseconds - stopwatch.ElapsedMilliseconds;
                if (sleepMilliseconds > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(sleepMilliseconds));
                }
            }
        }

        private bool IsFileKnownFromItsSegments(FileInfo file, HashSet<long> knownSegments) =>
            file.SegmentKeys.All(k => knownSegments.Contains(k));

        private bool IsSegmentKnownFromItsFragments(SegmentInfo segment, HashSet<long> unknownFragments) =>
            !segment.FragmentKeys.Any(k => unknownFragments.Contains(k));

        private Task WriteFileToLocalPath(FileInfo file)
        {
            return Task.Factory.StartNew(() =>
            {
                var fileLocalPath = Path.GetFullPath(Path.Combine(options.LocalBasePath, file.LocalPath));
                Directory.CreateDirectory(Path.GetDirectoryName(fileLocalPath));
                var fileContent = file.SegmentKeys
                    .SelectMany(sk => manifest.SegmentMap[sk].FragmentKeys)
                    .Select(fk => store.Get(fk))
                    .Where(c => c != null)
                    .SelectMany(c => c)
                    .ToArray();
                File.WriteAllBytes(fileLocalPath, fileContent);
            });
        }

        private Task<byte[]> DownloadSegmentAsync(FileInfo file, SegmentInfo segment)
        {
            var length = Math.Min(manifest.SegmentSize, file.FileSize - segment.OffsetInFile);
            logger.LogDebug("uri={0} offset={1} length={2}", file.RemotePath, segment.OffsetInFile, length);
            return segmentDownloader.Invoke(file.RemotePath, segment.OffsetInFile, length);
        }

        private static IEnumerable<IList<T>> SplitByLength<T>(T[] source, int length)
        {
            var splitCount = 1 + (source.Length - 1) / length;
            for (int i = 0; i < splitCount; i++)
            {
                yield return new ArraySegment<T>(
                    source,
                    i * length,
                    Math.Min(source.Length - i * length, length));
            }
        }
    }
}
