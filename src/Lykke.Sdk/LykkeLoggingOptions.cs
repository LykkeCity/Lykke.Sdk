﻿using System;
using JetBrains.Annotations;
using Lykke.Logs;

namespace Lykke.Sdk
{
    /// <summary>
    /// Lykke logging options class.
    /// </summary>
    /// <typeparam name="TAppSettings">The type of the application settings.</typeparam>
    [PublicAPI]
    public class LykkeLoggingOptions<TAppSettings>
    {
        /// <summary>
        /// Name of the Azure table for logs. Required
        /// </summary>
        public string AzureTableName { get; set; }

        /// <summary>
        /// Azure table connection string resolver delegate for Azure table logs. Required
        /// </summary>
        public Func<TAppSettings, string> AzureTableConnectionStringResolver { get; set; }

        /// <summary>
        /// Extended logging options. Optional
        /// </summary>
        [CanBeNull]
        public Action<ILogBuilder> Extended { get; set; }
    }
}