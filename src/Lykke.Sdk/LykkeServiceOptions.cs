using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using FluentValidation.AspNetCore;
using JetBrains.Annotations;
using Lykke.Common.ApiLibrary.Exceptions;
using Lykke.SettingsReader;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Lykke.Sdk
{
    /// <summary>
    /// Options for configuring lykke services.
    /// </summary>
    /// <typeparam name="TAppSettings">The type of the application settings.</typeparam>
    [PublicAPI]
    public class LykkeServiceOptions<TAppSettings>
    {
        /// <summary>
        /// Swagger Options. Required
        /// </summary>
        [Required]
        public LykkeSwaggerOptions SwaggerOptions { get; set; }

        /// <summary>
        /// Additional Swagger Options.
        /// </summary>
        public IReadOnlyCollection<LykkeSwaggerOptions> AdditionalSwaggerOptions { get; set; } =
            Array.Empty<LykkeSwaggerOptions>();

        /// <summary>
        /// Logging configuration delegate. Required.
        /// </summary>
        [Required]
        public Action<LykkeLoggingOptions<TAppSettings>> Logs { get; set; }

        /// <summary>
        /// Extended swagger configuration delegate. Optional.
        /// </summary>
        [CanBeNull]
        public Action<SwaggerGenOptions> Swagger { get; set; }

        /// <summary>
        /// Extended service configuration calls. Optional.
        /// </summary>
        [CanBeNull]
        public Action<IServiceCollection, IReloadingManager<TAppSettings>> Extend { get; set; }

#if (NETSTANDARD2_0 || NETSTANDARD2_1)
        /// <summary>
        ///  Register additional AutoFac modules. Optional.
        /// </summary>
        [CanBeNull]
        public Action<IModuleRegistration> RegisterAdditionalModules { get; set; }
#endif

        /// <summary>
        ///  Extends mvc options. Optional.
        /// </summary>
        [CanBeNull]
        public Action<MvcOptions> ConfigureMvcOptions { get; set; }

        /// <summary>
        /// Extends mvc builder. Optional.
        /// </summary>
        [CanBeNull]
        public Action<IMvcBuilder> ConfigureMvcBuilder { get; set; }
        
        /// <summary>
        ///  Extends fluent validation options. Optional.
        /// </summary>
        [CanBeNull]
        public Action<FluentValidationMvcConfiguration> ConfigureFluentValidation { get; set; }

        /// <summary>
        /// Optional.
        /// Configures application parts manager
        /// </summary>
        public Action<ApplicationPartManager> ConfigureApplicationParts { get; set; }

        internal bool HaveToDisableValidationFilter { get; private set; }
        
        internal bool HaveToDisableFluentValidation { get; private set; }

        /// <summary>
        /// Disables the action filter, which throws <see cref="ValidationApiException"/>
        /// if model state is not valid.
        /// Also see <see cref="LykkeConfigurationOptions.DisableValidationExceptionMiddleware()"/>
        /// </summary>
        public void DisableValidationFilter()
        {
            HaveToDisableValidationFilter = true;
        }

        /// <summary>
        /// Disables the fluent validation.
        /// </summary>
        public void DisableFluentValidation()
        {
            HaveToDisableFluentValidation = true;
        }
    }
}
