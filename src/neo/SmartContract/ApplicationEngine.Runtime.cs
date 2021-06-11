using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract.Native;
using Neo.VM.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Array = Neo.VM.Types.Array;

namespace Neo.SmartContract
{
    partial class ApplicationEngine
    {
        /// <summary>
        /// The maximum length of event name.
        /// </summary>
        public const int MaxEventName = 32;

        /// <summary>
        /// The maximum size of notification objects.
        /// </summary>
        public const int MaxNotificationSize = 1024;

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.Platform.
        /// Gets the name of the current platform.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_Platform = Register("System.Runtime.Platform", nameof(GetPlatform), 1 << 3, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.GetTrigger.
        /// Gets the trigger of the execution.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_GetTrigger = Register("System.Runtime.GetTrigger", nameof(Trigger), 1 << 3, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.GetTime.
        /// Gets the timestamp of the current block.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_GetTime = Register("System.Runtime.GetTime", nameof(GetTime), 1 << 3, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.GetScriptContainer.
        /// Gets the current script container.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_GetScriptContainer = Register("System.Runtime.GetScriptContainer", nameof(GetScriptContainer), 1 << 3, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.GetExecutingScriptHash.
        /// Gets the script hash of the current context.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_GetExecutingScriptHash = Register("System.Runtime.GetExecutingScriptHash", nameof(CurrentScriptHash), 1 << 4, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.GetCallingScriptHash.
        /// Gets the script hash of the calling contract.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_GetCallingScriptHash = Register("System.Runtime.GetCallingScriptHash", nameof(CallingScriptHash), 1 << 4, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.GetEntryScriptHash.
        /// Gets the script hash of the entry context.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_GetEntryScriptHash = Register("System.Runtime.GetEntryScriptHash", nameof(EntryScriptHash), 1 << 4, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.CheckWitness.
        /// Determines whether the specified account has witnessed the current transaction.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_CheckWitness = Register("System.Runtime.CheckWitness", nameof(CheckWitness), 1 << 10, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.GetInvocationCounter.
        /// Gets the number of times the current contract has been called during the execution.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_GetInvocationCounter = Register("System.Runtime.GetInvocationCounter", nameof(GetInvocationCounter), 1 << 4, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.Log.
        /// Writes a log.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_Log = Register("System.Runtime.Log", nameof(RuntimeLog), 1 << 15, CallFlags.AllowNotify);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.Notify.
        /// Sends a notification.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_Notify = Register("System.Runtime.Notify", nameof(RuntimeNotify), 1 << 15, CallFlags.AllowNotify);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.GetNotifications.
        /// Gets the notifications sent by the specified contract during the execution.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_GetNotifications = Register("System.Runtime.GetNotifications", nameof(GetNotifications), 1 << 8, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.GasLeft.
        /// Gets the remaining GAS that can be spent in order to complete the execution.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_GasLeft = Register("System.Runtime.GasLeft", nameof(GasLeft), 1 << 4, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.BurnGas.
        /// Burning GAS to benefit the NEO ecosystem.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_BurnGas = Register("System.Runtime.BurnGas", nameof(BurnGas), 1 << 4, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Runtime.GetRandom.
        /// Gets the random number generated from the VRF.
        /// </summary>
        public static readonly InteropDescriptor System_Runtime_GetRandom = Register("System.Runtime.GetRandom", nameof(GetRandom), 1 << 4, CallFlags.None);

        /// <summary>
        /// The implementation of System.Runtime.GetRandom.
        /// </summary>
        /// <returns>The last eight bytes of the random number.</returns>
        protected internal ulong GetRandom()
        {
            uint index = PersistingBlock.Index;
            if (index < ProtocolSettings.ValidatorsCount) throw new InvalidOperationException("Require more blocks than validators");

            byte[] hash = new byte[sizeof(long) + ((ProtocolSettings.ValidatorsCount + 1) * UInt256.Length)];

            // Tx related
            const int offset = sizeof(long) + UInt256.Length;
            System.Array.Copy(BitConverter.GetBytes(GasConsumed), 0, hash, 0, sizeof(long));
            System.Array.Copy(ScriptContainer.Hash.ToArray(), 0, hash, sizeof(long), UInt256.Length);

            // Block related
            for (int count = 0; count < ProtocolSettings.ValidatorsCount; index--, count++)
            {
                var blockHash = count == 0 ? PersistingBlock.Hash : NativeContract.Ledger.GetBlockHash(Snapshot, index);
                System.Array.Copy(blockHash.ToArray(), 0, hash, offset + (UInt256.Length * count), UInt256.Length);
            }

            // Sha256
            return BitConverter.ToUInt64(hash.Sha256());
        }

        /// <summary>
        /// The implementation of System.Runtime.Platform.
        /// Gets the name of the current platform.
        /// </summary>
        /// <returns>It always returns "NEO".</returns>
        internal protected static string GetPlatform()
        {
            return "NEO";
        }

        /// <summary>
        /// The implementation of System.Runtime.GetTime.
        /// Gets the timestamp of the current block.
        /// </summary>
        /// <returns>The timestamp of the current block.</returns>
        protected internal ulong GetTime()
        {
            return PersistingBlock.Timestamp;
        }

        /// <summary>
        /// The implementation of System.Runtime.GetScriptContainer.
        /// Gets the current script container.
        /// </summary>
        /// <returns>The current script container.</returns>
        protected internal StackItem GetScriptContainer()
        {
            if (ScriptContainer is not IInteroperable interop) throw new InvalidOperationException();
            return interop.ToStackItem(ReferenceCounter);
        }

        /// <summary>
        /// The implementation of System.Runtime.CheckWitness.
        /// Determines whether the specified account has witnessed the current transaction.
        /// </summary>
        /// <param name="hashOrPubkey">The hash or public key of the account.</param>
        /// <returns><see langword="true"/> if the account has witnessed the current transaction; otherwise, <see langword="false"/>.</returns>
        protected internal bool CheckWitness(byte[] hashOrPubkey)
        {
            UInt160 hash = hashOrPubkey.Length switch
            {
                20 => new UInt160(hashOrPubkey),
                33 => Contract.CreateSignatureRedeemScript(ECPoint.DecodePoint(hashOrPubkey, ECCurve.Secp256r1)).ToScriptHash(),
                _ => throw new ArgumentException(null, nameof(hashOrPubkey))
            };
            return CheckWitnessInternal(hash);
        }

        /// <summary>
        /// Determines whether the specified account has witnessed the current transaction.
        /// </summary>
        /// <param name="hash">The hash of the account.</param>
        /// <returns><see langword="true"/> if the account has witnessed the current transaction; otherwise, <see langword="false"/>.</returns>
        protected internal bool CheckWitnessInternal(UInt160 hash)
        {
            if (hash.Equals(CallingScriptHash)) return true;

            if (ScriptContainer is Transaction tx)
            {
                Signer[] signers;
                OracleResponse response = tx.GetAttribute<OracleResponse>();
                if (response is null)
                {
                    signers = tx.Signers;
                }
                else
                {
                    OracleRequest request = NativeContract.Oracle.GetRequest(Snapshot, response.Id);
                    signers = NativeContract.Ledger.GetTransaction(Snapshot, request.OriginalTxid).Signers;
                }
                Signer signer = signers.FirstOrDefault(p => p.Account.Equals(hash));
                if (signer is null) return false;
                if (signer.Scopes == WitnessScope.Global) return true;
                if (signer.Scopes.HasFlag(WitnessScope.CalledByEntry))
                {
                    if (CallingScriptHash == null || CallingScriptHash == EntryScriptHash)
                        return true;
                }
                if (signer.Scopes.HasFlag(WitnessScope.CustomContracts))
                {
                    if (signer.AllowedContracts.Contains(CurrentScriptHash))
                        return true;
                }
                if (signer.Scopes.HasFlag(WitnessScope.CustomGroups))
                {
                    // Check allow state callflag

                    ValidateCallFlags(CallFlags.ReadStates);

                    var contract = NativeContract.ContractManagement.GetContract(Snapshot, CallingScriptHash);
                    // check if current group is the required one
                    if (contract.Manifest.Groups.Select(p => p.PubKey).Intersect(signer.AllowedGroups).Any())
                        return true;
                }
                return false;
            }

            // Check allow state callflag

            ValidateCallFlags(CallFlags.ReadStates);

            // only for non-Transaction types (Block, etc)

            var hashes_for_verifying = ScriptContainer.GetScriptHashesForVerifying(Snapshot);
            return hashes_for_verifying.Contains(hash);
        }

        /// <summary>
        /// The implementation of System.Runtime.GetInvocationCounter.
        /// Gets the number of times the current contract has been called during the execution.
        /// </summary>
        /// <returns>The number of times the current contract has been called during the execution.</returns>
        protected internal int GetInvocationCounter()
        {
            if (!invocationCounter.TryGetValue(CurrentScriptHash, out var counter))
            {
                invocationCounter[CurrentScriptHash] = counter = 1;
            }
            return counter;
        }

        /// <summary>
        /// The implementation of System.Runtime.Log.
        /// Writes a log.
        /// </summary>
        /// <param name="state">The message of the log.</param>
        protected internal void RuntimeLog(byte[] state)
        {
            if (state.Length > MaxNotificationSize) throw new ArgumentException(null, nameof(state));
            string message = Utility.StrictUTF8.GetString(state);
            Log?.Invoke(this, new LogEventArgs(ScriptContainer, CurrentScriptHash, message));
        }

        /// <summary>
        /// The implementation of System.Runtime.Notify.
        /// Sends a notification.
        /// </summary>
        /// <param name="eventName">The name of the event.</param>
        /// <param name="state">The arguments of the event.</param>
        protected internal void RuntimeNotify(byte[] eventName, Array state)
        {
            if (eventName.Length > MaxEventName) throw new ArgumentException(null, nameof(eventName));
            using MemoryStream ms = new(MaxNotificationSize);
            using BinaryWriter writer = new(ms, Utility.StrictUTF8, true);
            BinarySerializer.Serialize(writer, state, MaxNotificationSize);
            SendNotification(CurrentScriptHash, Utility.StrictUTF8.GetString(eventName), state);
        }

        /// <summary>
        /// Sends a notification for the specified contract.
        /// </summary>
        /// <param name="hash">The hash of the specified contract.</param>
        /// <param name="eventName">The name of the event.</param>
        /// <param name="state">The arguments of the event.</param>
        protected internal void SendNotification(UInt160 hash, string eventName, Array state)
        {
            NotifyEventArgs notification = new(ScriptContainer, hash, eventName, (Array)state.DeepCopy());
            Notify?.Invoke(this, notification);
            notifications ??= new List<NotifyEventArgs>();
            notifications.Add(notification);
        }

        /// <summary>
        /// The implementation of System.Runtime.GetNotifications.
        /// Gets the notifications sent by the specified contract during the execution.
        /// </summary>
        /// <param name="hash">The hash of the specified contract. It can be set to <see langword="null"/> to get all notifications.</param>
        /// <returns>The notifications sent during the execution.</returns>
        protected internal NotifyEventArgs[] GetNotifications(UInt160 hash)
        {
            IEnumerable<NotifyEventArgs> notifications = Notifications;
            if (hash != null) // must filter by scriptHash
                notifications = notifications.Where(p => p.ScriptHash == hash);
            NotifyEventArgs[] array = notifications.ToArray();
            if (array.Length > Limits.MaxStackSize) throw new InvalidOperationException();
            return array;
        }

        /// <summary>
        /// The implementation of System.Runtime.BurnGas.
        /// Burning GAS to benefit the NEO ecosystem.
        /// </summary>
        /// <param name="gas">The amount of GAS to burn.</param>
        protected internal void BurnGas(long gas)
        {
            if (gas <= 0)
                throw new InvalidOperationException("GAS must be positive.");
            AddGas(gas);
        }
    }
}
