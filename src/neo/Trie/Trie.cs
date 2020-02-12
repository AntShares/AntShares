
using System.Collections.Generic;

namespace Neo.Trie
{
    public interface ITrie
    {
        bool TryGet(byte[] path, out byte[] value);

        bool Put(byte[] path, byte[] value);

        bool TryDelete(byte[] path);

        byte[] GetRoot();

        Dictionary<byte[], byte[]> GetProof(byte[] Key);

        void Commit();
    }
}
