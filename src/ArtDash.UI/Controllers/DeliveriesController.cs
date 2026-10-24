using ArtDash.Application.Features.Deliveries;
using ArtDash.Shared.Contracts.Deliveries;

namespace ArtDash.UI.Controllers;

[Route(ApiRoutes.Vehicles)]
[Authorize(Roles = AppRoles.Staff)]
public sealed class VehiclesController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<VehicleDto>> List([FromQuery] bool onlyActive = false, CancellationToken cancellationToken = default) =>
        sender.Send(new GetVehiclesQuery(onlyActive), cancellationToken);

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(SaveVehicleRequest request, CancellationToken cancellationToken)
    {
        var id = await sender.Send(new CreateVehicleCommand(request), cancellationToken);
        return CreatedWithId(nameof(List), id);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveVehicleRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new UpdateVehicleCommand(id, request), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteVehicleCommand(id), cancellationToken);
        return NoContent();
    }
}

/// <summary>Driver profiles (users with the Driver role are created and edited under /api/users).</summary>
[Route(ApiRoutes.Drivers)]
[Authorize(Roles = AppRoles.Staff)]
public sealed class DriversController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<DriverDto>> List([FromQuery] bool onlyActive = false, CancellationToken cancellationToken = default) =>
        sender.Send(new GetDriversQuery(onlyActive), cancellationToken);
}

/// <summary>Delivery runs: planning, monitoring and the live map (staff).</summary>
[Route(ApiRoutes.Deliveries)]
[Authorize(Roles = AppRoles.Staff)]
public sealed class DeliveriesController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<DeliveryListItemDto>> Search([FromQuery] DeliveryQuery filter, CancellationToken cancellationToken) =>
        sender.Send(new SearchDeliveriesQuery(filter), cancellationToken);

    /// <summary>Last known position of every vehicle on a run in progress (live map).</summary>
    [HttpGet("positions")]
    public Task<IReadOnlyList<VehiclePositionDto>> Positions(CancellationToken cancellationToken) =>
        sender.Send(new GetActivePositionsQuery(), cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<DeliveryDto> Get(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetDeliveryQuery(id), cancellationToken);

    [HttpGet("{id:guid}/trail")]
    public Task<IReadOnlyList<TrailPointDto>> Trail(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetDeliveryTrailQuery(id), cancellationToken);

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateDeliveryRequest request, CancellationToken cancellationToken)
    {
        var id = await sender.Send(new CreateDeliveryCommand(request), cancellationToken);
        return CreatedWithId(nameof(Get), id);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelDeliveryRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new CancelDeliveryCommand(id, request.Reason), cancellationToken);
        return NoContent();
    }
}

/// <summary>The driver app: today's runs, GPS positions and proof of delivery.</summary>
[Route(ApiRoutes.Driver)]
[Authorize(Roles = AppRoles.Driver)]
public sealed class DriverController(ISender sender) : ApiControllerBase
{
    [HttpGet("deliveries")]
    public Task<IReadOnlyList<DeliveryListItemDto>> MyDeliveries(CancellationToken cancellationToken) =>
        sender.Send(new GetMyDeliveriesQuery(), cancellationToken);

    [HttpGet("deliveries/{id:guid}")]
    public Task<DeliveryDto> Get(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetMyDeliveryQuery(id), cancellationToken);

    [HttpGet("deliveries/{id:guid}/trail")]
    public Task<IReadOnlyList<TrailPointDto>> Trail(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetDeliveryTrailQuery(id), cancellationToken);

    [HttpPost("deliveries/{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new StartDeliveryCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>A GPS fix from the driver's phone. Sent every few seconds while the run is in progress.</summary>
    [HttpPost("deliveries/{id:guid}/positions")]
    public async Task<IActionResult> RecordPosition(Guid id, RecordPositionRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new RecordPositionCommand(id, request), cancellationToken);
        return NoContent();
    }

    [HttpPost("deliveries/{id:guid}/stops/{stopId:guid}/complete")]
    public async Task<IActionResult> CompleteStop(Guid id, Guid stopId, CompleteStopRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new CompleteStopCommand(id, stopId, request), cancellationToken);
        return NoContent();
    }

    [HttpPost("deliveries/{id:guid}/stops/{stopId:guid}/fail")]
    public async Task<IActionResult> FailStop(Guid id, Guid stopId, FailStopRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new FailStopCommand(id, stopId, request.Reason), cancellationToken);
        return NoContent();
    }

    [HttpPost("deliveries/{id:guid}/finish")]
    public async Task<IActionResult> Finish(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new FinishDeliveryCommand(id), cancellationToken);
        return NoContent();
    }
}
