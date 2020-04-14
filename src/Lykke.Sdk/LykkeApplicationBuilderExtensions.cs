using System;
using System.Collections.Generic;
using System.Linq;
using Autofac;
using JetBrains.Annotations;
using Lykke.Common.ApiLibrary.Middleware;
using Lykke.Common.Log;
using Lykke.Sdk.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace Lykke.Sdk
{
    /// <summary>
    /// Extension methods for <see cref="IApplicationBuilder"/> class.
    /// </summary>
    [PublicAPI]
    public static class LykkeApplicationBuilderExtensions
    {
        /// <summary>
        /// Configure Lykke service.
        /// </summary>
        /// <param name="app"></param>
        public static IApplicationBuilder UseLykkeConfiguration(this IApplicationBuilder app, IApplicationLifetime appLifetime)
            => app.UseLykkeConfiguration(appLifetime, null);

        /// <summary>
        /// Configure Lykke service.
        /// </summary>
        /// <param name="app">IApplicationBuilder implementation.</param>
        /// <param name="configureOptions">Configuration handler for <see cref="LykkeConfigurationOptions"/></param>
        public static IApplicationBuilder UseLykkeConfiguration(
            this IApplicationBuilder app,
            IApplicationLifetime appLifetime,
            Action<LykkeConfigurationOptions> configureOptions)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            var options = new LykkeConfigurationOptions();
            configureOptions?.Invoke(options);

            var env = app.ApplicationServices.GetService<IHostingEnvironment>();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            try
            {
                app.UseMiddleware<UnhandledExceptionResponseMiddleware>(
                    options.DefaultErrorHandler,
                    options.UnhandledExceptionHttpStatusCodeResolver);

                if (!options.HaveToDisableUnhandledExceptionLoggingMiddleware)
                {
                    app.UseMiddleware<UnhandledExceptionLoggingMiddleware>();
                }

                if (!options.HaveToDisableValidationExceptionMiddleware)
                {
                    app.UseMiddleware<ClientServiceApiExceptionMiddleware>();
                }

                app.UseLykkeForwardedHeaders();

                // Middleware like authentication needs to be registered before Mvc
                options.WithMiddleware?.Invoke(app);

                app.UseStaticFiles();
#if (NETCOREAPP3_0 || NETCOREAPP3_1)
                app.UseRouting();
                app.UseEndpoints(endpoints => {
                    endpoints.MapControllers();
                });
#elif NETSTANDARD2_0
                app.UseMvc();
#else
#error unknown target framework
#endif

                app.UseSwagger(c =>
                {
                    c.PreSerializeFilters.Add((swagger, httpReq) =>
                    {
                        swagger.Servers = new List<OpenApiServer> { new OpenApiServer { Url = $"{httpReq.Scheme}://{httpReq.Host.Value}" } };
                    });
                });
                app.UseSwaggerUI(x =>
                {
                    x.RoutePrefix = "swagger/ui";
                    x.SwaggerEndpoint($"/swagger/{options.SwaggerOptions.ApiVersion}/swagger.json", options.SwaggerOptions.ApiVersion);

                    if (options.AdditionalSwaggerOptions.Any())
                    {
                        foreach (var swaggerVersion in options.AdditionalSwaggerOptions)
                        {
                            if (string.IsNullOrEmpty(swaggerVersion.ApiVersion))
                                throw new ArgumentNullException($"{nameof(options.AdditionalSwaggerOptions)}.{nameof(LykkeSwaggerOptions.ApiVersion)}");
                            
                            x.SwaggerEndpoint($"/swagger/{swaggerVersion.ApiVersion}/swagger.json", swaggerVersion.ApiVersion);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(options.SwaggerOptions.ApiTitle))
                    {
                        x.DocumentTitle = options.SwaggerOptions.ApiTitle;
                    }
                });

                appLifetime.ApplicationStarted.Register(() =>
                {
                    try
                    {
#if (NETCOREAPP3_0 || NETCOREAPP3_1)
                        Console.WriteLine($"Hosting environment: {env.EnvironmentName}");
                        Console.WriteLine($"Content root path: {env.ContentRootPath}");
                        Console.WriteLine($"Now listening on: http://[::]:{LykkeStarter.Port}");
#endif
                        app.ApplicationServices.GetService<AppLifetimeHandler>().HandleStarted();
                    }
                    catch (Exception)
                    {
                        appLifetime.StopApplication();
                    }
                });
                appLifetime.ApplicationStopping.Register(app.ApplicationServices.GetService<AppLifetimeHandler>().HandleStopping);
                appLifetime.ApplicationStopped.Register(() =>
                {
                    app.ApplicationServices.GetService<AppLifetimeHandler>().HandleStopped();
                });
            }
            catch (Exception ex)
            {
                try
                {
                    var log = app.ApplicationServices.GetService<ILogFactory>().CreateLog(typeof(LykkeApplicationBuilderExtensions).FullName);

                    log.Critical(ex);
                }
                catch (Exception ex1)
                {
                    Console.WriteLine(ex);
                    Console.WriteLine(ex1);
                }

                throw;
            }

            return app;
        }
    }
}
