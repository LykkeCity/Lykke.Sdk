using System;
using Common.Log;
using JetBrains.Annotations;
using Lykke.Common.Log;
using Lykke.MonitoringServiceApiCaller;
using Lykke.Sdk.Settings;
using Lykke.SettingsReader;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Lykke.Sdk
{
    [UsedImplicitly]
    internal class AppLifetimeHandler
    {
        private readonly ILogFactory _logFactory;
        private readonly IHealthNotifier _healthNotifier;
        private readonly IStartupManager _startupManager;
        private readonly IShutdownManager _shutdownManager;
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly IConfigurationRoot _configurationRoot;
        private readonly IReloadingManager<MonitoringServiceClientSettings> _monitoringServiceClientSettings;
        
        private readonly ILog _log;

        public AppLifetimeHandler(
            ILogFactory logFactory,
            IHealthNotifier healthNotifier,
            IStartupManager startupManager,
            IShutdownManager shutdownManager,
            IHostingEnvironment hostingEnvironment,
            IConfigurationRoot configurationRoot,
            IReloadingManager<MonitoringServiceClientSettings> monitoringServiceClientSettings)
        {
            _logFactory = logFactory ?? throw new ArgumentNullException(nameof(logFactory));
            _healthNotifier = healthNotifier ?? throw new ArgumentNullException(nameof(healthNotifier));
            _startupManager = startupManager ?? throw new ArgumentNullException(nameof(startupManager));
            _shutdownManager = shutdownManager ?? throw new ArgumentNullException(nameof(shutdownManager));
            _hostingEnvironment = hostingEnvironment ?? throw new ArgumentNullException(nameof(hostingEnvironment));
            _configurationRoot = configurationRoot ?? throw new ArgumentNullException(nameof(configurationRoot));
            _monitoringServiceClientSettings = monitoringServiceClientSettings ?? throw new ArgumentNullException(nameof(monitoringServiceClientSettings));

            _log = logFactory.CreateLog(this);
        }

        public void HandleStarted()
        {
            try
            {
                _healthNotifier.Notify("Initializing");

                _startupManager.StartAsync().GetAwaiter().GetResult();

                _healthNotifier.Notify("Application is started");

                if (_hostingEnvironment.IsDevelopment())
                {
                    return;
                }

                if (_monitoringServiceClientSettings?.CurrentValue == null)
                {
                    throw new ApplicationException("MonitoringServiceClient settings is not provided.");
                }

                _configurationRoot
                    .RegisterInMonitoringServiceAsync(_monitoringServiceClientSettings.CurrentValue.MonitoringServiceUrl, _healthNotifier)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                _log.Critical(ex);
                throw;
            }
        }

        public void HandleStopping()
        {
            try
            {
                _shutdownManager.StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _log.Critical(ex);
                throw;
            }
        }

        public void HandleStopped()
        {
            try
            {
                _healthNotifier.Notify("Application is being terminated");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                try
                {
                    _logFactory.Dispose();
                }
                catch (Exception ex1)
                {
                    Console.WriteLine(ex1);
                }

                throw;
            }
        }
    }
}