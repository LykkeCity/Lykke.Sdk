using System.Linq;
using System.Net;
using Lykke.Common;
using Lykke.Common.Api.Contract.Responses;
using Lykke.Sdk.Health;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Lykke.Sdk.Controllers
{
    // NOTE: See https://lykkex.atlassian.net/wiki/spaces/LKEWALLET/pages/35755585/Add+your+app+to+Monitoring
    /// <summary>
    /// Common controller for app health monitoring.
    /// </summary>
    [Route("api/[controller]")]
    public class IsAliveController : Controller
    {
        private readonly IHealthService _healthService;
        private readonly bool _isDebug;

        /// <summary>C-tor</summary>
        public IsAliveController(IHealthService healthService)
        {
            _healthService = healthService;
            _isDebug = LykkeStarter.IsDebug;
        }

        /// <summary>
        /// Checks service is alive
        /// </summary>
        [HttpGet]
        [SwaggerOperation("IsAlive")]
        [ProducesResponseType(typeof(IsAliveResponse), (int)HttpStatusCode.OK)]
        public IActionResult Get()
        {
            return Ok(
                new IsAliveResponse
                {
                    Name = AppEnvironment.Name,
                    Version = AppEnvironment.Version,
                    Env = AppEnvironment.EnvInfo,
                    IsDebug = _isDebug,
                    IssueIndicators = _healthService.GetHealthIssues()
                        .Select(i => new IsAliveResponse.IssueIndicator { Type = i.Type, Value = i.Value }),
                });
        }
    }
}
