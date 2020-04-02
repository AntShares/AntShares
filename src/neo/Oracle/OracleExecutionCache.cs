using Neo.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Neo.Oracle
{
    public class OracleExecutionCache : IEnumerable<KeyValuePair<UInt160, OracleResponse>>, ISerializable
    {
        /// <summary>
        /// Results (OracleRequest.Hash/OracleResponse)
        /// </summary>
        private readonly Dictionary<UInt160, OracleResponse> _cache = new Dictionary<UInt160, OracleResponse>();

        /// <summary>
        /// Engine
        /// </summary>
        private readonly Func<OracleRequest, OracleResponse> _oracle;

        /// <summary>
        /// Count
        /// </summary>
        public int Count => _cache.Count;

        public int Size => IO.Helper.GetVarSize(Count) + _cache.Values.Sum(u => u.Size);

        /// <summary>
        /// Constructor for oracles
        /// </summary>
        /// <param name="oracle">Oracle Engine</param>
        public OracleExecutionCache(Func<OracleRequest, OracleResponse> oracle = null)
        {
            _oracle = oracle;
        }

        /// <summary>
        /// Constructor for ISerializable
        /// </summary>
        public OracleExecutionCache() { }

        /// <summary>
        /// Constructor for cached results
        /// </summary>
        /// <param name="results">Results</param>
        public OracleExecutionCache(params OracleResponse[] results)
        {
            _oracle = null;

            foreach (var result in results)
            {
                _cache[result.RequestHash] = result;
            }
        }

        /// <summary>
        /// Get Oracle result
        /// </summary>
        /// <param name="request">Request</param>
        /// <param name="result">Result</param>
        /// <returns></returns>
        public bool TryGet(OracleRequest request, out OracleResponse result)
        {
            if (_cache.TryGetValue(request.Hash, out result))
            {
                return true;
            }

            // Not found inside the cache, invoke it

            result = _oracle?.Invoke(request);

            if (result != null)
            {
                _cache[request.Hash] = result;
                return true;
            }

            // Without oracle logic

            return false;
        }

        public IEnumerator<KeyValuePair<UInt160, OracleResponse>> GetEnumerator()
        {
            return _cache.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _cache.GetEnumerator();
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.WriteArray(_cache.Values.ToArray());
        }

        public void Deserialize(BinaryReader reader)
        {
            var results = reader.ReadSerializableArray<OracleResponse>(byte.MaxValue);

            _cache.Clear();
            foreach (var result in results)
            {
                _cache[result.RequestHash] = result;
            }
        }
    }
}
