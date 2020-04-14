using System;
using JetBrains.Annotations;
using Lykke.Sdk.Health;
using Lykke.SettingsReader;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lykke.Sdk.Middleware
{
    /// <summary>
    /// Maintenance middleware builder.
    /// </summary>
    [PublicAPI]
    public static class MaintenanceModeBuilder
    {
        /// <summary>
        /// Adds middleware that checks every 5 minutes if the service is in maintenance mode based on the settings.
        /// </summary>
        /// <typeparam name="TAppSettings">The type of the application settings.</typeparam>
        /// <param name="builder">The builder.</param>
        /// <param name="getMode">The maintenance mode getter.</param>
        /// <returns>the builder</returns>
        /// <remarks></remarks>
        public static IApplicationBuilder UseMaintenanceMode<TAppSettings>(this IApplicationBuilder builder, Func<TAppSettings, MaintenanceMode> getMode)
            where TAppSettings : class
            => UseMaintenanceMode(builder, getMode, TimeSpan.FromMinutes(5));

        /// <summary>
        /// Adds middleware that checks if the service is in maintenance mode based on the settings.
        /// </summary>
        /// <typeparam name="TAppSettings">The type of the application settings.</typeparam>
        /// <param name="builder">The builder.</param>
        /// <param name="getMode">The maintenance mode getter.</param>
        /// <param name="checkPeriod">The interval to check the settings</param>
        /// <returns>the builder</returns>
        public static IApplicationBuilder UseMaintenanceMode<TAppSettings>(
            this IApplicationBuilder builder,
            Func<TAppSettings, MaintenanceMode> getMode,
            TimeSpan checkPeriod)
            where TAppSettings : class
        {
            if (getMode == null)
                throw new ArgumentNullException(nameof(getMode));

            var configRoot = builder.ApplicationServices.GetRequiredService<IConfigurationRoot>();
            var settings = configRoot.LoadSettings<TAppSettings>(_ => { });

            return builder.UseMiddleware<MaintenanceModeMiddleware<TAppSettings>>(settings, getMode, checkPeriod);
        }
    }
}