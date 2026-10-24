using ArtDash.Application.Features.Customers;
using ArtDash.Application.Features.Orders;
using ArtDash.Shared.Contracts.Customers;
using ArtDash.Shared.Contracts.Orders;
using Microsoft.AspNetCore.RateLimiting;

namespace ArtDash.UI.Controllers;

/// <summary>Back-office order management (staff).</summary>
[Route(ApiRoutes.Orders)]
[Authorize(Roles = AppRoles.Staff)]
public sealed class OrdersController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<OrderListItemDto>> Search([FromQuery] OrderQuery filter, CancellationToken cancellationToken) =>
        sender.Send(new SearchOrdersQuery(filter), cancellationToken);

    /// <summary>Open orders for the kanban board (pending to out for delivery).</summary>
    [HttpGet("board")]
    public Task<IReadOnlyList<OrderListItemDto>> Board(CancellationToken cancellationToken) =>
        sender.Send(new GetOrderBoardQuery(), cancellationToken);

    /// <summary>Accounts receivable: invoiced orders, open or overdue.</summary>
    [HttpGet("receivables")]
    public Task<ReceivablesDto> Receivables([FromQuery] ReceivableQuery filter, CancellationToken cancellationToken) =>
        sender.Send(new GetReceivablesQuery(filter), cancellationToken);

    /// <summary>Orders that can be put on a delivery run.</summary>
    [HttpGet("deliverable")]
    public Task<IReadOnlyList<DeliverableOrderDto>> Deliverable(CancellationToken cancellationToken) =>
        sender.Send(new GetDeliverableOrdersQuery(), cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<OrderDto> Get(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetOrderQuery(id), cancellationToken);

    /// <summary>Places an order on behalf of a customer (phone, WhatsApp, sales rep).</summary>
    [HttpPost]
    public async Task<ActionResult<PlaceOrderResultDto>> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new PlaceOrderCommand(request, OrderChannel.Backoffice), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.OrderId }, result);
    }

    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, ChangeOrderStatusRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new ChangeOrderStatusCommand(id, request.Status, request.Note), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelOrderRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new CancelOrderCommand(id, request.Reason), cancellationToken);
        return NoContent();
    }

    /// <summary>Records money received outside the gateway (cash, card machine, transfer, invoice settlement).</summary>
    [HttpPost("{id:guid}/payments")]
    public async Task<IActionResult> RegisterPayment(Guid id, RegisterPaymentRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new RegisterOrderPaymentCommand(id, request), cancellationToken);
        return NoContent();
    }
}

/// <summary>The signed-in customer's own profile and orders (storefront).</summary>
[Route(ApiRoutes.Me)]
[Authorize(Roles = AppRoles.Customer)]
public sealed class MeController(ISender sender) : ApiControllerBase
{
    [HttpGet("profile")]
    public Task<MyProfileDto> GetProfile(CancellationToken cancellationToken) =>
        sender.Send(new GetMyProfileQuery(), cancellationToken);

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new UpdateMyProfileCommand(request), cancellationToken);
        return NoContent();
    }

    [HttpGet("orders")]
    public Task<PagedResult<OrderListItemDto>> Orders([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        sender.Send(new GetMyOrdersQuery(page, pageSize), cancellationToken);

    [HttpGet("orders/{id:guid}")]
    public Task<OrderDto> Order(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetMyOrderQuery(id), cancellationToken);

    [HttpPost("orders")]
    public async Task<ActionResult<PlaceOrderResultDto>> PlaceOrder(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new PlaceOrderCommand(request, OrderChannel.Storefront), cancellationToken);
        return CreatedAtAction(nameof(Order), new { id = result.OrderId }, result);
    }

    [HttpPost("orders/{id:guid}/cancel")]
    public async Task<IActionResult> CancelOrder(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new CancelMyOrderCommand(id), cancellationToken);
        return NoContent();
    }
}

/// <summary>Public order tracking by the unguessable code in the tracking link.</summary>
[Route(ApiRoutes.Tracking)]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
public sealed class TrackingController(ISender sender) : ApiControllerBase
{
    [HttpGet("{code}")]
    public Task<OrderTrackingDto> Get(string code, CancellationToken cancellationToken) =>
        sender.Send(new GetOrderTrackingQuery(code), cancellationToken);
}
