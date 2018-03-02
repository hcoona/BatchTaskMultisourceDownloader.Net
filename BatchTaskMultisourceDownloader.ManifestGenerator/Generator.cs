using System;
using System.Collections.Generic;
using Microsoft.Extensions.FileProviders;

namespace BatchTaskMultisourceDownloader.ManifestGenerator
{
    public class Generator
    {
        public ManifestInfo Generate(
            long segmentSize,
            long fragmentSize,
            IEnumerable<KeyValuePair<string, string>> localRemotePairs)
        {
            if (segmentSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(segmentSize), "Segment size must be positive number");
            }
            if (fragmentSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fragmentSize), "Fragment size must be positive number");
            }
            if (fragmentSize > segmentSize)
            {
                throw new ArgumentException($"{nameof(segmentSize)} must be larger than {nameof(fragmentSize)}");
            }
            if (segmentSize / fragmentSize * fragmentSize != segmentSize)
            {
                throw new ArgumentException($"{nameof(fragmentSize)} must divide {nameof(segmentSize)}");
            }

            var manifest = new ManifestInfo
            {
                SegmentSize = segmentSize,
                FragmentSize = fragmentSize,
            };
            foreach (var p in localRemotePairs)
            {
                var fileProvider = GetFileProvider(p.Value);
                var f = fileProvider.GetFileInfo(p.Value);
                if (f.IsDirectory)
                {
                    var d = fileProvider.GetDirectoryContents(p.Value);
                }
                else
                {

                }
            }
            return manifest;
        }

        private IFileProvider GetFileProvider(string value)
        {
            throw new NotImplementedException();
        }
    }
}
