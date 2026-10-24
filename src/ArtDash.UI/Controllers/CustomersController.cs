using ArtDash.Application.Features.Customers;
using ArtDash.Shared.Contracts.Customers;

namespace ArtDash.UI.Controllers;

[Route(ApiRoutes.Customers)]
[Authorize(Roles = AppRoles.Staff)]
public sealed class CustomersController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<CustomerListItemDto>> Search([FromQuery] CustomerQuery filter, CancellationToken cancellationToken) =>
        sender.Send(new SearchCustomersQuery(filter), cancellationToken);

    /// <summary>Active customers for selects and the back-office order form.</summary>
    [HttpGet("lookup")]
    public Task<IReadOnlyList<LookupDto>> Lookup([FromQuery] string? term, CancellationToken cancellationToken) =>
        sender.Send(new LookupCustomersQuery(term), cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<CustomerDto> Get(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetCustomerQuery(id), cancellationToken);

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(SaveCustomerRequest request, CancellationToken cancellationToken)
    {
        var id = await sender.Send(new CreateCustomerCommand(request), cancellationToken);
        return CreatedWithId(nameof(Get), id);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveCustomerRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new UpdateCustomerCommand(id, request), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteCustomerCommand(id), cancellationToken);
        return NoContent();
    }
}
