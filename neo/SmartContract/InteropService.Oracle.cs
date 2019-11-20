using Neo.Oracle.Protocols.HTTP;

namespace Neo.SmartContract
{
    static partial class InteropService
    {
        public static readonly uint Neo_Oracle_HTTP11_Get = Register("Oracle.HTTP11.Get", Oracle_HTTP11_Get, 0, TriggerType.Application);
        public static readonly uint Neo_Oracle_HTTP11_Post = Register("Oracle.HTTP11.Post", Oracle_HTTP11_Post, 0, TriggerType.Application);

        public static readonly uint Neo_Oracle_HTTP20_Get = Register("Oracle.HTTP20.Get", Oracle_HTTP20_Get, 0, TriggerType.Application);
        public static readonly uint Neo_Oracle_HTTP20_Post = Register("Oracle.HTTP20.Post", Oracle_HTTP20_Post, 0, TriggerType.Application);

        #region Http 1.1

        private static bool Oracle_HTTP11_Get(ApplicationEngine engine)
        {
            var url = engine.CurrentContext.EvaluationStack.Pop().GetString();
            var filter = engine.CurrentContext.EvaluationStack.Pop().GetString();

            return Oracle_HTTP(engine, OracleHTTPRequest.HTTPVersion.v1_1, OracleHTTPRequest.HTTPMethod.GET, url, filter, null);
        }

        private static bool Oracle_HTTP11_Post(ApplicationEngine engine)
        {
            var url = engine.CurrentContext.EvaluationStack.Pop().GetString();
            var filter = engine.CurrentContext.EvaluationStack.Pop().GetString();
            var body = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();

            return Oracle_HTTP(engine, OracleHTTPRequest.HTTPVersion.v1_1, OracleHTTPRequest.HTTPMethod.POST, url, filter, body);
        }

        #endregion

        #region Http 2.0

        private static bool Oracle_HTTP20_Get(ApplicationEngine engine)
        {
            var url = engine.CurrentContext.EvaluationStack.Pop().GetString();
            var filter = engine.CurrentContext.EvaluationStack.Pop().GetString();

            return Oracle_HTTP(engine, OracleHTTPRequest.HTTPVersion.v2_0, OracleHTTPRequest.HTTPMethod.GET, url, filter, null);
        }

        private static bool Oracle_HTTP20_Post(ApplicationEngine engine)
        {
            var url = engine.CurrentContext.EvaluationStack.Pop().GetString();
            var filter = engine.CurrentContext.EvaluationStack.Pop().GetString();
            var body = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();

            return Oracle_HTTP(engine, OracleHTTPRequest.HTTPVersion.v2_0, OracleHTTPRequest.HTTPMethod.POST, url, filter, body);
        }

        #endregion

        private static bool Oracle_HTTP(ApplicationEngine engine, OracleHTTPRequest.HTTPVersion version, OracleHTTPRequest.HTTPMethod method, string url, string filter, byte[] body)
        {
            if (engine.OracleCache == null) return false;

            var request = new OracleHTTPRequest()
            {
                Method = method,
                URL = url,
                Filter = filter,
                Body = body,
                Version = version
            };

            // Extract from cache

            if (engine.OracleCache.TryGet(request, out var response))
            {
                engine.CurrentContext.EvaluationStack.Push(response.ToStackItem());
                return true;
            }

            return false;
        }
    }
}
