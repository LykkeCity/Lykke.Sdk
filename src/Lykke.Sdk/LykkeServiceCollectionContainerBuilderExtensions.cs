﻿using Autofac;
using Autofac.Extensions.DependencyInjection;
using Common.Log;
using FluentValidation.AspNetCore;
using JetBrains.Annotations;
using Lykke.Common.ApiLibrary.Swagger;
using Lykke.Sdk.Settings;
using Lykke.SettingsReader;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Converters;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Reflection;

namespace Lykke.Sdk
{
    [PublicAPI]
    public static class LykkeServiceCollectionContainerBuilderExtensions
    {
        /// <summary>
        /// Build service provider for Lykke's service.
        /// </summary>
        public static IServiceProvider BuildServiceProvider<TAppSettings>(
            this IServiceCollection services,
            Action<LykkeServiceOptions<TAppSettings>> serviceOptionsBuilder)
            where TAppSettings : BaseAppSettings
        {
            return BuildServiceProvider(services, serviceOptionsBuilder, null);
        }

        /// <summary>
        /// Build service provider for Lykke's service.
        /// </summary>
        public static IServiceProvider BuildServiceProvider<TAppSettings>(
            this IServiceCollection services,
            Action<LykkeServiceOptions<TAppSettings>> serviceOptionsBuilder,
            Action<SwaggerGenOptions> swaggerOptionsConfugure)
            where TAppSettings : BaseAppSettings
        {
            if (services == null)
                throw new ArgumentNullException("services");

            if (serviceOptionsBuilder == null)
                throw new ArgumentNullException("serviceOptionsBuilder");

            var serviceOptions = new LykkeServiceOptions<TAppSettings>();
            serviceOptionsBuilder(serviceOptions);

            if (string.IsNullOrWhiteSpace(serviceOptions.ApiTitle))
                throw new ArgumentException("Api title must be provided.");

            if (string.IsNullOrEmpty(serviceOptions.Logs.TableName))
                throw new ArgumentException("Logs table name must be provided.");

            if (serviceOptions.Logs.ConnectionString == null)
                throw new ArgumentException("Logs connection string must be provided.");

            services.AddMvc()
                .AddJsonOptions(options =>
                {
                    options.SerializerSettings.Converters.Add(new StringEnumConverter());
                    options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
                })
                .AddFluentValidation(x => x.RegisterValidatorsFromAssembly(Assembly.GetEntryAssembly()));

            services.AddSwaggerGen(options =>
            {
                options.DefaultLykkeConfiguration("v1", serviceOptions.ApiTitle);
                swaggerOptionsConfugure?.Invoke(options);
            });

            var configurationRoot = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var settings = configurationRoot.LoadSettings<TAppSettings>();

            var builder = new ContainerBuilder();

            builder.RegisterInstance(configurationRoot).As<IConfigurationRoot>();            
            builder.RegisterInstance(settings.CurrentValue.SlackNotifications);

            if (settings.CurrentValue.MonitoringServiceClient != null)
                builder.RegisterInstance(settings.Nested(x => x.MonitoringServiceClient));            

            builder.RegisterInstance(serviceOptions);

            var logger = LoggerFactory.CreateLogWithSlack(
                builder, 
                serviceOptions.Logs.TableName, 
                settings.ConnectionString(serviceOptions.Logs.ConnectionString), 
                settings.CurrentValue.SlackNotifications
            );

            builder.RegisterInstance(logger);
            builder.Populate(services);
            builder.RegisterAssemblyModules(settings, logger, Assembly.GetEntryAssembly());

            var container = builder.Build();

            var appLifetime = container.Resolve<IApplicationLifetime>();
            
            appLifetime.ApplicationStopped.Register(() =>
            {
                try
                {
                    logger?.WriteMonitor("StopApplication", null, "Terminating");

                    container.Dispose();
                }
                catch (Exception ex)
                {
                    if (logger != null)
                    {
                        logger.WriteFatalError("CleanUp", "", ex);
                        (logger as IDisposable)?.Dispose();
                    }
                    throw;
                }
            });

            return new AutofacServiceProvider(container);
        }
    }
}
