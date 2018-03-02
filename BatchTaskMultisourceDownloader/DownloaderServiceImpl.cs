using System;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;

namespace BatchTaskMultisourceDownloader
{
    internal class DownloaderServiceImpl : DownloaderService.DownloaderServiceBase
    {
        private readonly IKeyValueStore store;

        public DownloaderServiceImpl(IKeyValueStore store)
        {
            this.store = store;
        }

        public override async Task Pull(
            PullRequest request,
            IServerStreamWriter<PullResponse> responseStream,
            ServerCallContext context)
        {
            foreach (var fk in request.FragmentKeys)
            {
                var value = store.Get(BitConverter.GetBytes(fk));
                if (value != null)
                {
                    await responseStream.WriteAsync(new PullResponse
                    {
                        FragmentKey = fk,
                        FragmentValue = ByteString.CopyFrom(value)
                    });
                }
            }
        }
    }
}
