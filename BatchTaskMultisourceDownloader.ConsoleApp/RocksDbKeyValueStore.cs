using System.IO;
using RocksDbSharp;

namespace BatchTaskMultisourceDownloader.ConsoleApp
{
    public class RocksDbKeyValueStore : IKeyValueStore
    {
        private readonly RocksDb db;

        public RocksDbKeyValueStore()
        {
            Directory.Delete("db.bin", true);
            db = RocksDb.Open(
                new DbOptions()
                    .SetCreateIfMissing()
                    .SetCompression(CompressionTypeEnum.rocksdb_snappy_compression),
                "db.bin");
        }

        public byte[] Get(byte[] key) => db.Get(key);

        public void Put(byte[] key, byte[] value) => db.Put(key, value);

        public void Remove(byte[] key) => db.Remove(key);
    }
}
