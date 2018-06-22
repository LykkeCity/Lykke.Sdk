using System;
using Common.Log;
using JetBrains.Annotations;
using Lykke.Common.ApiLibrary.Middleware;
using Lykke.MonitoringServiceApiCaller;
using Lykke.Sdk.Settings;
using Lykke.SettingsReader;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Lykke.Sdk
{
    [PublicAPI]
    public abstract class LykkeStartup<TSettings> where TSettings : BaseAppSettings
    {
        /// <summary>
        /// Configure additional pipeline interceptors
        /// </summary>
        protected virtual void ConfigureRequestPipeline(IApplicationBuilder app)
        {
        }

        /// <summary>
        /// Configure application's API title and Logs settings
        /// </summary>
        protected virtual void BuildServiceProvilder(LykkeServiceOptions<TSettings> options)
        {
            options.ApiTitle = ApiTitle;
            options.Logs = (AzureLogTable, GetLogsConnectionString);
        }

        /// <summary>
        /// Returns the api title used on the swagger page
        /// </summary>
        public abstract string ApiTitle { get; }

        /// <summary>
        /// The table name for the logs of that service
        /// </summary>
        public abstract string AzureLogTable { get; }

        /// <summary>
        /// Returns the logs connection string from a passed settings instance
        /// </summary>
        protected abstract string GetLogsConnectionString(TSettings settings);

        /// <summary>
        /// Configure additional swagger options here (e.g. custom filters)
        /// </summary>
        protected virtual void ConfigureSwagger(SwaggerGenOptions swagger) {}

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            return services.BuildServiceProvider<TSettings>(
                BuildServiceProvilder,
                ConfigureSwagger);
        }

        public void Configure(IApplicationBuilder app)
        {
            var env = app.ApplicationServices.GetService<IHostingEnvironment>();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                var appName = System.Reflection.Assembly.GetEntryAssembly().GetName().Name;
                app.UseLykkeMiddleware(appName, TransformErrorToResponseObject);
            }

            app.UseLykkeForwardedHeaders();

            var log = app.ApplicationServices.GetService<ILog>();

            try
            {
                var appLifetime = app.ApplicationServices.GetService<IApplicationLifetime>();
                var configurationRoot = app.ApplicationServices.GetService<IConfigurationRoot>();

                if (configurationRoot == null)
                    throw new ApplicationException("Configuration root must be registered in the container");

                var monitoringSettings = app
                    .ApplicationServices
                    .GetService<IReloadingManager<MonitoringServiceClientSettings>>();

                var startupManager = app.ApplicationServices.GetService<IStartupManager>();
                var shutdownManager = app.ApplicationServices.GetService<IShutdownManager>();
                var hostingEnvironment = app.ApplicationServices.GetService<IHostingEnvironment>();

                appLifetime.ApplicationStarted.Register(() =>
                {
                    try
                    {
                        startupManager?.StartAsync().ConfigureAwait(false).GetAwaiter().GetResult();

                        log.WriteMonitor("StartApplication", null, "Application started");

                        if (!hostingEnvironment.IsDevelopment())
                        {
                            if (monitoringSettings?.CurrentValue == null)
                                throw new ApplicationException("Monitoring settings is not provided.");

                            AutoRegistrationInMonitoring.RegisterAsync(
                                    configurationRoot,
                                    monitoringSettings.CurrentValue.MonitoringServiceUrl, log)
                                .GetAwaiter()
                                .GetResult();
                        }

                    }
                    catch (Exception ex)
                    {
                        log.WriteFatalError("StartApplication", "", ex);
                        throw;
                    }
                });

                appLifetime.ApplicationStopping.Register(() =>
                {
                    try
                    {
                        shutdownManager?.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        log?.WriteFatalError("StopApplication", "", ex);

                        throw;
                    }
                });

                ConfigureRequestPipeline(app);

                app.UseStaticFiles();
                app.UseMvc();

                app.UseSwagger(c =>
                {
                    c.PreSerializeFilters.Add((swagger, httpReq) => swagger.Host = httpReq.Host.Value);
                });
                app.UseSwaggerUI(x =>
                {
                    x.RoutePrefix = "swagger/ui";
                    x.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
                });
            }
            catch (Exception ex)
            {
                log?.WriteFatalError("Startup", "", ex);
                throw;
            }

            if (env.IsDevelopment())
            {
                TelemetryConfiguration.Active.DisableTelemetry = true;
            }
        }

        protected virtual object TransformErrorToResponseObject(Exception ex)
        {
            return new { message = "Technical error" };
        }
    }
}