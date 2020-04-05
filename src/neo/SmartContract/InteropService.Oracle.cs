using Neo.Network.P2P.Payloads;
using Neo.Oracle;
using Neo.Oracle.Protocols.Https;
using Neo.SmartContract.Native;
using Neo.VM.Types;
using System;
using System.Text;

namespace Neo.SmartContract
{
    partial class InteropService
    {
        public static class Oracle
        {
            public static readonly uint Neo_Oracle_Get = Register("Neo.Oracle.Get", Oracle_Get, 0, TriggerType.Application, CallFlags.None);

            /// <summary>
            /// Oracle Get
            ///     string url, [UInt160 filter], [string filterMethod]
            /// </summary>
            private static bool Oracle_Get(ApplicationEngine engine)
            {
                if (engine.OracleCache == null)
                {
                    // We should enter here only during OnPersist with the OracleRequestTx

                    if (engine.ScriptContainer is Transaction tx)
                    {
                        // Read Oracle Response

                        engine.OracleCache = NativeContract.Oracle.GetOracleResponse(engine.Snapshot, tx.Hash);

                        // If it doesn't exist, fault

                        if (engine.OracleCache == null)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
                if (!engine.TryPop(out string urlItem) || !Uri.TryCreate(urlItem, UriKind.Absolute, out var url)) return false;
                if (!engine.TryPop(out StackItem filterContractItem)) return false;
                if (!engine.TryPop(out StackItem filterMethodItem)) return false;

                // Create filter

                OracleFilter filter = null;

                if (!filterContractItem.IsNull)
                {
                    if (filterContractItem is PrimitiveType filterContract &&
                        filterMethodItem is PrimitiveType filterMethod)
                    {
                        filter = new OracleFilter()
                        {
                            ContractHash = new UInt160(filterContract.Span),
                            FilterMethod = Encoding.UTF8.GetString(filterMethod.Span)
                        };
                    }
                    else
                    {
                        return false;
                    }
                }

                // Create request

                OracleRequest request;
                switch (url.Scheme.ToLowerInvariant())
                {
                    case "https":
                        {
                            request = new OracleHttpsRequest()
                            {
                                Method = HttpMethod.GET,
                                URL = url,
                                Filter = filter
                            };
                            break;
                        }
                    default: return false;
                }

                // Execute the oracle request

                if (engine.OracleCache.TryGet(request, out var response))
                {
                    engine.Push(response.ToStackItem(engine.ReferenceCounter));
                    return true;
                }

                return false;
            }
        }
    }
}
