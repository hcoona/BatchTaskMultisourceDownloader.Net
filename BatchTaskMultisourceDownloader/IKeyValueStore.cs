using System;
using System.Threading.Tasks;

namespace BatchTaskMultisourceDownloader
{
    public interface IKeyValueStore
    {
        byte[] Get(byte[] key);

        void Put(byte[] key, byte[] value);

        void Remove(byte[] key);
    }

    internal static class KeyValueStoreExtension
    {
        public static byte[] Get(this IKeyValueStore store, long key) => store.Get(BitConverter.GetBytes(key));

        public static void Put(this IKeyValueStore store, long key, byte[] value) => store.Put(BitConverter.GetBytes(key), value);

        public static void Remove(this IKeyValueStore store, long key) => store.Remove(BitConverter.GetBytes(key));
    }
}
