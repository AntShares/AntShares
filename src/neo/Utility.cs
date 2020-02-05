using Microsoft.Extensions.Configuration;
using Neo.Plugins;
using System;

namespace Neo
{
    public static class Utility
    {
        /// <summary>
        /// Load configuration with different Environment Variable
        /// </summary>
        /// <param name="config">Configuration</param>
        /// <returns>IConfigurationRoot</returns>
        public static IConfigurationRoot LoadConfig(string config)
        {
            var env = Environment.GetEnvironmentVariable("NEO_NETWORK");
            var configFile = string.IsNullOrWhiteSpace(env) ? $"{config}.json" : $"{config}.{env}.json";
            try
            {
                return new ConfigurationBuilder()
                    .AddJsonFile(configFile, true)
                    .Build();
            }
            catch (Exception e)
            {
                Log(nameof(Utility), LogLevel.Error, $"Failed parsing {configFile}, Error: " + e.Message);
                return new ConfigurationBuilder()
                    .Build();
            }
        }

        public static void Log(string source, LogLevel level, string message)
        {
            foreach (ILogPlugin plugin in Plugin.Loggers)
                plugin.Log(source, level, message);
        }
    }
}
