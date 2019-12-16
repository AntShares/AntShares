using Neo.VM;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Neo.SmartContract
{
    public static partial class InteropService
    {
        private static readonly Dictionary<uint, InteropDescriptor> methods = new Dictionary<uint, InteropDescriptor>();

        static InteropService()
        {
            foreach (Type t in typeof(InteropService).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                t.GetFields()[0].GetValue(null);
        }

        public static long GetPrice(uint hash, EvaluationStack stack)
        {
            return methods[hash].GetPrice(stack);
        }

        public static IEnumerable<InteropDescriptor> SupportedMethods()
        {
            return methods.Values;
        }

        internal static bool Invoke(ApplicationEngine engine, uint method)
        {
            if (!methods.TryGetValue(method, out InteropDescriptor descriptor))
                return false;
            if (!descriptor.AllowedTriggers.HasFlag(engine.Trigger))
                return false;
            return descriptor.Handler(engine);
        }

        private static InteropDescriptor Register(string method, Func<ApplicationEngine, bool> handler, long price, TriggerType allowedTriggers)
        {
            InteropDescriptor descriptor = new InteropDescriptor(method, handler, price, allowedTriggers);
            methods.Add(descriptor.Hash, descriptor);
            return descriptor;
        }

        private static InteropDescriptor Register(string method, Func<ApplicationEngine, bool> handler, Func<EvaluationStack, long> priceCalculator, TriggerType allowedTriggers)
        {
            InteropDescriptor descriptor = new InteropDescriptor(method, handler, priceCalculator, allowedTriggers);
            methods.Add(descriptor.Hash, descriptor);
            return descriptor;
        }
    }
}
