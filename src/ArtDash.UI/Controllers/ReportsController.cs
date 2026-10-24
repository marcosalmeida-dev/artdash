using ArtDash.Application.Features.Reports;
using ArtDash.Shared.Contracts.Reports;

namespace ArtDash.UI.Controllers;

/// <summary>Sales, customer, payment and delivery reports for the current tenant.</summary>
[Route(ApiRoutes.Reports)]
[Authorize(Roles = AppRoles.Staff)]
public sealed class ReportsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public Task<ReportDto> Get([FromQuery] ReportQuery filter, CancellationToken cancellationToken) =>
        sender.Send(new GetReportsQuery(filter), cancellationToken);
}