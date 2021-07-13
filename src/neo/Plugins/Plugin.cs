using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using static System.IO.Path;
namespace Neo.Plugins
{
    /// <summary>
    /// Represents the base class of all plugins. Any plugin should inherit this class. The plugins are automatically loaded when the process starts.
    /// </summary>
    public abstract class Plugin : IDisposable
    {
        /// <summary>
        /// Indicates the name of the plugin.
        /// </summary>
        public virtual string Name => GetType().Name;

        /// <summary>
        /// Indicates the description of the plugin.
        /// </summary>
        public virtual string Description => "";

        /// <summary>
        /// Indicates the version of the plugin.
        /// </summary>
        public virtual Version Version => GetType().Assembly.GetName().Version;

        public virtual void Dispose()
        {
        }

        /// <summary>
        /// Called when the plugin is loaded and need to load the configure file, or the configuration file has been modified and needs to be reconfigured.
        /// </summary>
        public virtual void Configure(IConfigurationSection configuration)
        {
        }

        /// <summary>
        /// Called when a message to the plugins is received. The messnage is sent by calling <see cref="NeoSystem.SendPluginMessage(object)"/>.
        /// </summary>
        /// <param name="message">The received message.</param>
        /// <returns><see langword="true"/> if the <paramref name="message"/> has been handled; otherwise, <see langword="false"/>.</returns>
        /// <remarks>If a message has been handled by a plugin, the other plugins won't receive it anymore.</remarks>
        internal virtual bool OnMessage(object message)
        {
            return false;
        }

        /// <summary>
        /// Called when a <see cref="NeoSystem"/> is loaded.
        /// </summary>
        /// <param name="system">The loaded <see cref="NeoSystem"/>.</param>
        internal protected virtual void OnSystemLoaded(NeoSystem system)
        {
        }
    }
}
