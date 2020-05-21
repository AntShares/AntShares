using Neo.Ledger;
using Neo.Persistence;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;
using System.Text;
using System.Text.RegularExpressions;
using Array = Neo.VM.Types.Array;

namespace Neo.SmartContract.Nns
{
    partial class NnsContract
    {
        private static readonly uint MaxResolveCount = 7;

        // only can be called by the admin of the name
        [ContractMethod(0_03000000, ContractParameterType.Boolean, CallFlags.AllowModifyStates, ParameterTypes = new[] { ContractParameterType.ByteArray, ContractParameterType.Integer, ContractParameterType.String }, ParameterNames = new[] { "name", "recordType", "text" })]
        private StackItem SetText(ApplicationEngine engine, Array args)
        {
            byte[] tokenId = args[0].GetSpan().ToArray();
            RecordType recordType = (RecordType)(byte)args[1].GetBigInteger();
            byte[] text = args[2].GetSpan().ToArray();
            switch (recordType)
            {
                case RecordType.A:
                    if (text.Length != UInt160.Length) return false;
                    break;
                case RecordType.CNAME:
                    string cname = Encoding.UTF8.GetString(text);
                    if (!IsDomain(cname)) return false;
                    break;
            }
            DomainState domainInfo = GetDomainInfo(engine.Snapshot, tokenId);
            if (domainInfo is null || domainInfo.IsExpired(engine.Snapshot)) return false;
            if (!InteropService.Runtime.CheckWitnessInternal(engine, domainInfo.Operator)) return false;

            UInt160 innerKey = GetInnerKey(tokenId);
            StorageKey key = CreateStorageKey(Prefix_Record, innerKey);
            StorageItem storage = engine.Snapshot.Storages.GetAndChange(key, () => new StorageItem(new RecordInfo()));
            RecordInfo recordInfo = storage.GetInteroperable<RecordInfo>();
            recordInfo.Type = recordType;
            recordInfo.Text = text;
            return true;
        }

        // return the text and recordtype of the name
        [ContractMethod(0_03000000, ContractParameterType.String, CallFlags.AllowModifyStates, ParameterTypes = new[] { ContractParameterType.ByteArray }, ParameterNames = new[] { "name" })]
        public StackItem Resolve(ApplicationEngine engine, Array args)
        {
            byte[] name = args[0].GetSpan().ToArray();
            return Resolve(engine.Snapshot, name).ToStackItem(engine.ReferenceCounter);
        }

        public RecordInfo Resolve(StoreView snapshot, byte[] domain, int resolveCount = 0)
        {
            if (resolveCount > MaxResolveCount)
                return new RecordInfo { Type = RecordType.ERROR, Text = Encoding.ASCII.GetBytes("Too many domain redirects") };

            DomainState domainInfo = GetDomainInfo(snapshot, domain);
            if (domainInfo is null || domainInfo.IsExpired(snapshot))
                return new RecordInfo { Type = RecordType.ERROR, Text = Encoding.ASCII.GetBytes("Domain not found or expired") };

            UInt160 innerKey = GetInnerKey(domain);
            StorageKey key = CreateStorageKey(Prefix_Record, innerKey);
            StorageItem storage = snapshot.Storages.TryGet(key);
            if (storage is null)
                return new RecordInfo { Type = RecordType.ERROR, Text = Encoding.ASCII.GetBytes("Text does not exist") };

            RecordInfo recordInfo = storage.GetInteroperable<RecordInfo>();
            switch (recordInfo.Type)
            {
                case RecordType.CNAME:
                    var parameter_cname = recordInfo.Text;
                    return Resolve(snapshot, parameter_cname, ++resolveCount);
            }
            return recordInfo;
        }

        public bool IsDomain(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string pattern = @"^(?=^.{3,255}$)[a-zA-Z0-9][-a-zA-Z0-9]{0,62}(\.[a-zA-Z0-9][-a-zA-Z0-9]{0,62}){1,3}$";
            Regex regex = new Regex(pattern);
            return regex.Match(name).Success;
        }

        public bool IsRootDomain(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string pattern = @"^[a-zA-Z]{0,62}$";
            Regex regex = new Regex(pattern);
            return regex.Match(name).Success;
        }
    }
}