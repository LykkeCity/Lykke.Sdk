﻿using System;
using System.Linq;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using FluentValidation.AspNetCore;
using JetBrains.Annotations;
using Lykke.Common;
using Lykke.Common.ApiLibrary.Swagger;
using Lykke.Logs;
using Lykke.Sdk.ActionFilters;
using Lykke.Sdk.Health;
using Lykke.Sdk.Settings;
using Lykke.SettingsReader;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Converters;
using Swashbuckle.AspNetCore.Swagger;

namespace Lykke.Sdk
{
    /// <summary>
    /// Extension methods for <see cref="IServiceCollection"/> class.
    /// </summary>
    [PublicAPI]
    public static class LykkeServiceCollectionContainerBuilderExtensions
    {
        /// <summary>
        /// Build service provider for Lykke's service.
        /// </summary>
        public static IServiceProvider BuildServiceProvider<TAppSettings>(
            this IServiceCollection services,
            Action<LykkeServiceOptions<TAppSettings>> buildServiceOptions)

            where TAppSettings : class, IAppSettings
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (buildServiceOptions == null)
            {
                throw new ArgumentNullException(nameof(buildServiceOptions));
            }

            var serviceOptions = new LykkeServiceOptions<TAppSettings>();

            buildServiceOptions(serviceOptions);

            if (serviceOptions.SwaggerOptions == null)
            {
                throw new ArgumentException("Swagger options must be provided.");
            }

            if (serviceOptions.Logs == null)
            {
                throw new ArgumentException("Logs configuration delegate must be provided.");
            }

            var mvc = services.AddMvc(options =>
                {
                    if (!serviceOptions.HaveToDisableValidationFilter)
                    {
                        options.Filters.Add(new ActionValidationFilter());
                    }

                    serviceOptions.ConfigureMvcOptions?.Invoke(options);
                })
                .AddJsonOptions(options =>
                {
                    options.SerializerSettings.Converters.Add(new StringEnumConverter());
                    options.SerializerSettings.ContractResolver =
                        new Newtonsoft.Json.Serialization.DefaultContractResolver();
                })
                .ConfigureApplicationPartManager(partsManager =>
                {
                    serviceOptions.ConfigureApplicationParts?.Invoke(partsManager);
                });

            if (!serviceOptions.HaveToDisableFluentValidation)
            {
                mvc.AddFluentValidation(x =>
                {
                    x.RegisterValidatorsFromAssembly(Assembly.GetEntryAssembly());
                    serviceOptions.ConfigureFluentValidation?.Invoke(x);
                });
            }

            serviceOptions.ConfigureMvcBuilder?.Invoke(mvc);

            services.AddSwaggerGen(options =>
            {
                options.DefaultLykkeConfiguration(
                    serviceOptions.SwaggerOptions.ApiVersion ?? throw new ArgumentNullException($"{nameof(LykkeSwaggerOptions)}.{nameof(LykkeSwaggerOptions.ApiVersion)}"),
                    serviceOptions.SwaggerOptions.ApiTitle ?? throw new ArgumentNullException($"{nameof(LykkeSwaggerOptions)}.{nameof(LykkeSwaggerOptions.ApiTitle)}"));

                if (serviceOptions.AdditionalSwaggerOptions.Any())
                {
                    foreach (var swaggerVersion in serviceOptions.AdditionalSwaggerOptions)
                    {
                        options.SwaggerDoc(
                            $"{swaggerVersion.ApiVersion}",
                            new Info
                            {
                                Version = swaggerVersion.ApiVersion ?? throw new ArgumentNullException($"{nameof(serviceOptions.AdditionalSwaggerOptions)}.{nameof(LykkeSwaggerOptions.ApiVersion)}"),
                                Title = swaggerVersion.ApiTitle ?? throw new ArgumentNullException($"{nameof(serviceOptions.AdditionalSwaggerOptions)}.{nameof(LykkeSwaggerOptions.ApiTitle)}")
                            });
                    }
                }
                
                serviceOptions.Swagger?.Invoke(options);
            });

            var configurationRoot = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var settings = configurationRoot.LoadSettings<TAppSettings>(options =>
            {
                options.SetConnString(x => x.SlackNotifications?.AzureQueue.ConnectionString);
                options.SetQueueName(x => x.SlackNotifications?.AzureQueue.QueueName);
                options.SenderName = $"{AppEnvironment.Name} {AppEnvironment.Version}";
            });

            var loggingOptions = new LykkeLoggingOptions<TAppSettings>();

            serviceOptions.Logs(loggingOptions);

            if (loggingOptions.HaveToUseEmptyLogging)
            {
                services.AddEmptyLykkeLogging();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(loggingOptions.AzureTableName))
                {
                    throw new ArgumentException("Logs.AzureTableName must be provided.");
                }

                if (loggingOptions.AzureTableConnectionStringResolver == null)
                {
                    throw new ArgumentException("Logs.AzureTableConnectionStringResolver must be provided");
                }
                
                if (settings.CurrentValue.SlackNotifications == null)
                {
                    throw new ArgumentException("SlackNotifications settings section should be specified, when Lykke logging is enabled");
                }

                if (LykkeStarter.IsDebug)
                    services.AddConsoleLykkeLogging(options => { loggingOptions.Extended?.Invoke(options); });
                else
                    services.AddLykkeLogging(
                        settings.ConnectionString(loggingOptions.AzureTableConnectionStringResolver),
                        loggingOptions.AzureTableName,
                        settings.CurrentValue.SlackNotifications.AzureQueue.ConnectionString,
                        settings.CurrentValue.SlackNotifications.AzureQueue.QueueName,
                        options => { loggingOptions.Extended?.Invoke(options); });
            }

            var builder = new ContainerBuilder();

            serviceOptions.Extend?.Invoke(services, settings);

            builder.RegisterInstance(configurationRoot).As<IConfigurationRoot>();

            if (settings.CurrentValue.MonitoringServiceClient == null)
            {
                throw new InvalidOperationException("MonitoringServiceClient config section is required");
            }

            builder.RegisterInstance(settings.Nested(x => x.MonitoringServiceClient))
                .As<IReloadingManager<MonitoringServiceClientSettings>>();

            builder.RegisterInstance(serviceOptions);
            builder.RegisterType<AppLifetimeHandler>()
                .AsSelf()
                .SingleInstance();

            builder.Populate(services);
            builder.RegisterAssemblyModules(settings, serviceOptions.RegisterAdditionalModules, Assembly.GetEntryAssembly());

            builder.RegisterType<EmptyStartupManager>()
                .As<IStartupManager>()
                .SingleInstance()
                .IfNotRegistered(typeof(IStartupManager));

            builder.RegisterType<EmptyShutdownManager>()
                .As<IShutdownManager>()
                .SingleInstance()
                .IfNotRegistered(typeof(IShutdownManager));

            builder.RegisterType<HealthService>()
                .As<IHealthService>()
                .SingleInstance()
                .IfNotRegistered(typeof(IHealthService));

            var container = builder.Build();

            var appLifetime = container.Resolve<IApplicationLifetime>();

            appLifetime.ApplicationStarted.Register(() =>
            {
                try
                {
                    container.Resolve<AppLifetimeHandler>().HandleStarted();
                }
                catch (Exception)
                {
                    appLifetime.StopApplication();
                }
            });
            appLifetime.ApplicationStopping.Register(container.Resolve<AppLifetimeHandler>().HandleStopping);
            appLifetime.ApplicationStopped.Register(() => container.Resolve<AppLifetimeHandler>().HandleStopped(container));

            return new AutofacServiceProvider(container);
        }
    }
}
