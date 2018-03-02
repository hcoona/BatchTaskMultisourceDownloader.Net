using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NLog;
using NLog.Extensions.Logging;

namespace BatchTaskMultisourceDownloader.ConsoleApp
{
    internal class Program
    {
        internal static async Task Main(string[] args)
        {
            const string localhost = "127.0.0.1";

            var loggerFactory = new LoggerFactory();
            loggerFactory.AddNLog();
            LogManager.LoadConfiguration("nlog.config");

            var store = new RocksDbKeyValueStore();
            var memberList = new string[0];
            Func<IEnumerable<string>> memberListProvider = () => memberList;

            var d1 = CreateDownloader(loggerFactory, @"R:\d1", store, memberListProvider);
            var d2 = CreateDownloader(NullLoggerFactory.Instance, @"R:\d2", store, memberListProvider);

            var s1 = new Server
            {
                Ports = { new ServerPort(localhost, ServerPort.PickUnused, ServerCredentials.Insecure) },
                Services = { DownloaderService.BindService(d1.DownloaderService) }
            };
            var s2 = new Server
            {
                Ports = { new ServerPort(localhost, ServerPort.PickUnused, ServerCredentials.Insecure) },
                Services = { DownloaderService.BindService(d2.DownloaderService) }
            };

            s1.Start();
            s2.Start();

            memberList = s1.Ports.Concat(s2.Ports).Select(p => $"{localhost}:{p.BoundPort}").ToArray();

            var t1 = d1.InvokeAsync();
            var t2 = d2.InvokeAsync();

            await Task.WhenAll(t1, t2);

            await s1.ShutdownAsync();
            await s2.ShutdownAsync();

            Console.WriteLine("Finished");
        }

        private static Downloader CreateDownloader(
            ILoggerFactory loggerFactory,
            string basePath,
            IKeyValueStore store,
            Func<IEnumerable<string>> memberListProvider) =>
            new Downloader(
                loggerFactory.CreateLogger<Downloader>(),
                new DownloaderOptions
                {
                    LocalBasePath = basePath,
                    DownloadFromRemoteUriBias = 0.5,
                    LoopIntervalMilliseconds = 1000,
                    PeerFragmentDownloadFactor = 3
                },
                new ManifestInfo
                {
                    SegmentSize = 2 << 20, // 2MiB
                    FragmentSize = 1 << 20, // 1MiB
                    FileMap =
                    {
                        {
                            0,
                            new FileInfo
                            {
                                Key = 0,
                                LocalPath = @"./13w16a.jar",
                                RemotePath = @"http://s3.amazonaws.com/Minecraft.Download/versions/13w16a/13w16a.jar",
                                FileSize = 4548759,
                                SegmentKeys = { 0, 1, 2 }
                            }
                        }
                    },
                    SegmentMap =
                    {
                        {
                            0,
                            new SegmentInfo
                            {
                                Key = 0,
                                FileKey = 0,
                                OffsetInFile = 0,
                                FragmentKeys = { 0, 1 }
                            }
                        },
                        {
                            1,
                            new SegmentInfo
                            {
                                Key = 1,
                                FileKey = 0,
                                OffsetInFile = 2097152,
                                FragmentKeys = { 2, 3 }
                            }
                        },
                        {
                            2,
                            new SegmentInfo
                            {
                                Key = 2,
                                FileKey = 0,
                                OffsetInFile = 4194304,
                                FragmentKeys = { 4 }
                            }
                        }
                    },
                    FragmentMap =
                    {
                        { 0, new FragmentInfo{ Key = 0, SegmentKey = 0 } },
                        { 1, new FragmentInfo{ Key = 1, SegmentKey = 0 } },
                        { 2, new FragmentInfo{ Key = 2, SegmentKey = 1 } },
                        { 3, new FragmentInfo{ Key = 3, SegmentKey = 1 } },
                        { 4, new FragmentInfo{ Key = 4, SegmentKey = 2 } },
                    }
                },
                store,
                memberListProvider,
                target => new Channel(target, ChannelCredentials.Insecure),
                Download);

        private static async Task<byte[]> Download(string uri, long offset, long length)
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Range = new RangeHeaderValue(offset, offset + length);
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync();
            }
        }
    }
}
