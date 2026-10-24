using ArtDash.Shared.Contracts.Auth;
using ArtDash.Shared.Contracts.Branding;
using ArtDash.Shared.Contracts.Catalog;
using ArtDash.Shared.Contracts.Customers;
using ArtDash.Shared.Contracts.Dashboard;
using ArtDash.Shared.Contracts.Deliveries;
using ArtDash.Shared.Contracts.Orders;
using ArtDash.Shared.Contracts.Payments;
using ArtDash.Shared.Contracts.Reports;
using ArtDash.Shared.Contracts.Settings;
using ArtDash.Shared.Contracts.Users;

namespace ArtDash.UI.Client.Services.Api;

// One small typed client per API area. Routes come from ApiRoutes, shared with the controllers.

public sealed class StoreApi(ApiClient api)
{
    public Task<StoreInfoDto> GetInfoAsync(CancellationToken cancellationToken = default) =>
        api.GetAsync<StoreInfoDto>(ApiRoutes.Store, cancellationToken);

    public Task<StorefrontCatalogDto> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        api.GetAsync<StorefrontCatalogDto>($"{ApiRoutes.Store}/catalog", cancellationToken);
}

public sealed class BrandingApi(ApiClient api)
{
    public Task<BrandingDto> GetAsync(CancellationToken cancellationToken = default) =>
        api.GetAsync<BrandingDto>(ApiRoutes.Branding, cancellationToken);

    public Task<BrandingDto> UpdateAsync(UpdateBrandingRequest request) =>
        api.PutAsync<BrandingDto>(ApiRoutes.Branding, request);

    public Task<UploadResultDto> UploadAssetAsync(string kind, Stream content, string fileName, string contentType) =>
        api.UploadAsync<UploadResultDto>($"{ApiRoutes.Branding}/assets/{kind}", content, fileName, contentType);
}

public sealed class SettingsApi(ApiClient api)
{
    public Task<StoreSettingsDto> GetStoreAsync() => api.GetAsync<StoreSettingsDto>($"{ApiRoutes.Settings}/store");

    public Task UpdateStoreAsync(StoreSettingsDto request) => api.PutAsync($"{ApiRoutes.Settings}/store", request);

    public Task<PaymentSettingsDto> GetPaymentsAsync() => api.GetAsync<PaymentSettingsDto>($"{ApiRoutes.Settings}/payments");

    public Task UpdatePaymentsAsync(PaymentSettingsDto request) => api.PutAsync($"{ApiRoutes.Settings}/payments", request);
}

public sealed class AccountApi(ApiClient api)
{
    public Task<CurrentUserDto> MeAsync() => api.GetAsync<CurrentUserDto>($"{ApiRoutes.Auth}/me");

    public Task ChangePasswordAsync(ChangePasswordRequest request) => api.PostAsync($"{ApiRoutes.Auth}/change-password", request);
}

public sealed class CatalogApi(ApiClient api)
{
    public Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(bool includeInactive = true) =>
        api.GetAsync<IReadOnlyList<CategoryDto>>(ApiQuery.Build(ApiRoutes.Categories, ("includeInactive", includeInactive)));

    public Task<Guid> CreateCategoryAsync(SaveCategoryRequest request) => api.PostAsync<Guid>(ApiRoutes.Categories, request);

    public Task UpdateCategoryAsync(Guid id, SaveCategoryRequest request) => api.PutAsync($"{ApiRoutes.Categories}/{id}", request);

    public Task DeleteCategoryAsync(Guid id) => api.DeleteAsync($"{ApiRoutes.Categories}/{id}");

    public Task<PagedResult<ProductDto>> SearchProductsAsync(ProductQuery filter) =>
        api.GetAsync<PagedResult<ProductDto>>(ApiQuery.Build(ApiRoutes.Products,
            ("search", filter.Search), ("categoryId", filter.CategoryId), ("isActive", filter.IsActive),
            ("page", filter.Page), ("pageSize", filter.PageSize)));

    public Task<IReadOnlyList<ProductDto>> LookupProductsAsync(string? term, CancellationToken cancellationToken = default) =>
        api.GetAsync<IReadOnlyList<ProductDto>>(ApiQuery.Build($"{ApiRoutes.Products}/lookup", ("term", term)), cancellationToken);

    public Task<ProductDto> GetProductAsync(Guid id) => api.GetAsync<ProductDto>($"{ApiRoutes.Products}/{id}");

    public Task<Guid> CreateProductAsync(SaveProductRequest request) => api.PostAsync<Guid>(ApiRoutes.Products, request);

    public Task UpdateProductAsync(Guid id, SaveProductRequest request) => api.PutAsync($"{ApiRoutes.Products}/{id}", request);

    public Task DeleteProductAsync(Guid id) => api.DeleteAsync($"{ApiRoutes.Products}/{id}");

    public Task<UploadResultDto> UploadProductImageAsync(Stream content, string fileName, string contentType) =>
        api.UploadAsync<UploadResultDto>($"{ApiRoutes.Products}/images", content, fileName, contentType);
}

public sealed class CustomersApi(ApiClient api)
{
    public Task<PagedResult<CustomerListItemDto>> SearchAsync(CustomerQuery filter) =>
        api.GetAsync<PagedResult<CustomerListItemDto>>(ApiQuery.Build(ApiRoutes.Customers,
            ("search", filter.Search), ("isActive", filter.IsActive), ("page", filter.Page), ("pageSize", filter.PageSize)));

    public Task<IReadOnlyList<LookupDto>> LookupAsync(string? term, CancellationToken cancellationToken = default) =>
        api.GetAsync<IReadOnlyList<LookupDto>>(ApiQuery.Build($"{ApiRoutes.Customers}/lookup", ("term", term)), cancellationToken);

    public Task<CustomerDto> GetAsync(Guid id) => api.GetAsync<CustomerDto>($"{ApiRoutes.Customers}/{id}");

    public Task<Guid> CreateAsync(SaveCustomerRequest request) => api.PostAsync<Guid>(ApiRoutes.Customers, request);

    public Task UpdateAsync(Guid id, SaveCustomerRequest request) => api.PutAsync($"{ApiRoutes.Customers}/{id}", request);

    public Task DeleteAsync(Guid id) => api.DeleteAsync($"{ApiRoutes.Customers}/{id}");
}

public sealed class OrdersApi(ApiClient api)
{
    public Task<PagedResult<OrderListItemDto>> SearchAsync(OrderQuery filter) =>
        api.GetAsync<PagedResult<OrderListItemDto>>(ApiQuery.Build(ApiRoutes.Orders,
            ("search", filter.Search), ("status", filter.Status), ("paymentStatus", filter.PaymentStatus),
            ("paymentTiming", filter.PaymentTiming), ("from", filter.From), ("to", filter.To),
            ("page", filter.Page), ("pageSize", filter.PageSize)));

    public Task<IReadOnlyList<OrderListItemDto>> GetBoardAsync() =>
        api.GetAsync<IReadOnlyList<OrderListItemDto>>($"{ApiRoutes.Orders}/board");

    public Task<ReceivablesDto> GetReceivablesAsync(ReceivableQuery filter) =>
        api.GetAsync<ReceivablesDto>(ApiQuery.Build($"{ApiRoutes.Orders}/receivables",
            ("search", filter.Search), ("filter", filter.Filter), ("dueFrom", filter.DueFrom), ("dueTo", filter.DueTo),
            ("page", filter.Page), ("pageSize", filter.PageSize)));

    public Task<IReadOnlyList<DeliverableOrderDto>> GetDeliverableAsync() =>
        api.GetAsync<IReadOnlyList<DeliverableOrderDto>>($"{ApiRoutes.Orders}/deliverable");

    public Task<OrderDto> GetAsync(Guid id) => api.GetAsync<OrderDto>($"{ApiRoutes.Orders}/{id}");

    public Task<PlaceOrderResultDto> PlaceAsync(PlaceOrderRequest request) =>
        api.PostAsync<PlaceOrderResultDto>(ApiRoutes.Orders, request);

    public Task ChangeStatusAsync(Guid id, OrderStatus status, string? note = null) =>
        api.PostAsync($"{ApiRoutes.Orders}/{id}/status", new ChangeOrderStatusRequest { Status = status, Note = note });

    public Task CancelAsync(Guid id, string reason) =>
        api.PostAsync($"{ApiRoutes.Orders}/{id}/cancel", new CancelOrderRequest { Reason = reason });

    public Task RegisterPaymentAsync(Guid id, RegisterPaymentRequest request) =>
        api.PostAsync($"{ApiRoutes.Orders}/{id}/payments", request);
}

/// <summary>The signed-in customer's own data (storefront).</summary>
public sealed class MeApi(ApiClient api)
{
    public Task<MyProfileDto> GetProfileAsync() => api.GetAsync<MyProfileDto>($"{ApiRoutes.Me}/profile");

    public Task UpdateProfileAsync(UpdateMyProfileRequest request) => api.PutAsync($"{ApiRoutes.Me}/profile", request);

    public Task<PagedResult<OrderListItemDto>> GetOrdersAsync(int page = 1, int pageSize = 10) =>
        api.GetAsync<PagedResult<OrderListItemDto>>(ApiQuery.Build($"{ApiRoutes.Me}/orders", ("page", page), ("pageSize", pageSize)));

    public Task<OrderDto> GetOrderAsync(Guid id) => api.GetAsync<OrderDto>($"{ApiRoutes.Me}/orders/{id}");

    public Task<PlaceOrderResultDto> PlaceOrderAsync(PlaceOrderRequest request) =>
        api.PostAsync<PlaceOrderResultDto>($"{ApiRoutes.Me}/orders", request);

    public Task CancelOrderAsync(Guid id) => api.PostAsync($"{ApiRoutes.Me}/orders/{id}/cancel");
}

public sealed class PaymentsApi(ApiClient api)
{
    public Task<OnlinePaymentResultDto> CreateAsync(CreateOnlinePaymentRequest request) =>
        api.PostAsync<OnlinePaymentResultDto>(ApiRoutes.Payments, request);

    public Task<OnlinePaymentResultDto> SyncAsync(Guid transactionId) =>
        api.PostAsync<OnlinePaymentResultDto>($"{ApiRoutes.Payments}/{transactionId}/sync", null);
}

public sealed class TrackingApi(ApiClient api)
{
    public Task<OrderTrackingDto?> GetAsync(string code) =>
        api.FindAsync<OrderTrackingDto>($"{ApiRoutes.Tracking}/{Uri.EscapeDataString(code)}");
}

public sealed class FleetApi(ApiClient api)
{
    public Task<IReadOnlyList<VehicleDto>> GetVehiclesAsync(bool onlyActive = false) =>
        api.GetAsync<IReadOnlyList<VehicleDto>>(ApiQuery.Build(ApiRoutes.Vehicles, ("onlyActive", onlyActive)));

    public Task<Guid> CreateVehicleAsync(SaveVehicleRequest request) => api.PostAsync<Guid>(ApiRoutes.Vehicles, request);

    public Task UpdateVehicleAsync(Guid id, SaveVehicleRequest request) => api.PutAsync($"{ApiRoutes.Vehicles}/{id}", request);

    public Task DeleteVehicleAsync(Guid id) => api.DeleteAsync($"{ApiRoutes.Vehicles}/{id}");

    public Task<IReadOnlyList<DriverDto>> GetDriversAsync(bool onlyActive = false) =>
        api.GetAsync<IReadOnlyList<DriverDto>>(ApiQuery.Build(ApiRoutes.Drivers, ("onlyActive", onlyActive)));
}

public sealed class DeliveriesApi(ApiClient api)
{
    public Task<PagedResult<DeliveryListItemDto>> SearchAsync(DeliveryQuery filter) =>
        api.GetAsync<PagedResult<DeliveryListItemDto>>(ApiQuery.Build(ApiRoutes.Deliveries,
            ("status", filter.Status), ("driverId", filter.DriverId), ("from", filter.From), ("to", filter.To),
            ("page", filter.Page), ("pageSize", filter.PageSize)));

    public Task<IReadOnlyList<VehiclePositionDto>> GetPositionsAsync() =>
        api.GetAsync<IReadOnlyList<VehiclePositionDto>>($"{ApiRoutes.Deliveries}/positions");

    public Task<DeliveryDto> GetAsync(Guid id) => api.GetAsync<DeliveryDto>($"{ApiRoutes.Deliveries}/{id}");

    public Task<IReadOnlyList<TrailPointDto>> GetTrailAsync(Guid id) =>
        api.GetAsync<IReadOnlyList<TrailPointDto>>($"{ApiRoutes.Deliveries}/{id}/trail");

    public Task<Guid> CreateAsync(CreateDeliveryRequest request) => api.PostAsync<Guid>(ApiRoutes.Deliveries, request);

    public Task CancelAsync(Guid id, string reason) =>
        api.PostAsync($"{ApiRoutes.Deliveries}/{id}/cancel", new CancelDeliveryRequest { Reason = reason });
}

/// <summary>The driver app.</summary>
public sealed class DriverApi(ApiClient api)
{
    public Task<IReadOnlyList<DeliveryListItemDto>> GetDeliveriesAsync() =>
        api.GetAsync<IReadOnlyList<DeliveryListItemDto>>($"{ApiRoutes.Driver}/deliveries");

    public Task<DeliveryDto> GetAsync(Guid id) => api.GetAsync<DeliveryDto>($"{ApiRoutes.Driver}/deliveries/{id}");

    public Task<IReadOnlyList<TrailPointDto>> GetTrailAsync(Guid id) =>
        api.GetAsync<IReadOnlyList<TrailPointDto>>($"{ApiRoutes.Driver}/deliveries/{id}/trail");

    public Task StartAsync(Guid id) => api.PostAsync($"{ApiRoutes.Driver}/deliveries/{id}/start");

    public Task RecordPositionAsync(Guid id, RecordPositionRequest request) =>
        api.PostAsync($"{ApiRoutes.Driver}/deliveries/{id}/positions", request);

    public Task CompleteStopAsync(Guid id, Guid stopId, CompleteStopRequest request) =>
        api.PostAsync($"{ApiRoutes.Driver}/deliveries/{id}/stops/{stopId}/complete", request);

    public Task FailStopAsync(Guid id, Guid stopId, string reason) =>
        api.PostAsync($"{ApiRoutes.Driver}/deliveries/{id}/stops/{stopId}/fail", new FailStopRequest { Reason = reason });

    public Task FinishAsync(Guid id) => api.PostAsync($"{ApiRoutes.Driver}/deliveries/{id}/finish");
}

public sealed class UsersApi(ApiClient api)
{
    public Task<PagedResult<UserListItemDto>> SearchAsync(UserQuery filter) =>
        api.GetAsync<PagedResult<UserListItemDto>>(ApiQuery.Build(ApiRoutes.Users,
            ("search", filter.Search), ("role", filter.Role), ("isActive", filter.IsActive),
            ("page", filter.Page), ("pageSize", filter.PageSize)));

    public Task<UserDto> GetAsync(Guid id) => api.GetAsync<UserDto>($"{ApiRoutes.Users}/{id}");

    public Task<Guid> CreateAsync(SaveUserRequest request) => api.PostAsync<Guid>(ApiRoutes.Users, request);

    public Task UpdateAsync(Guid id, SaveUserRequest request) => api.PutAsync($"{ApiRoutes.Users}/{id}", request);

    public Task ResetPasswordAsync(Guid id, string newPassword) =>
        api.PostAsync($"{ApiRoutes.Users}/{id}/reset-password", new ResetPasswordRequest { NewPassword = newPassword });
}

public sealed class DashboardApi(ApiClient api)
{
    public Task<DashboardDto> GetAsync() => api.GetAsync<DashboardDto>(ApiRoutes.Dashboard);
}

public sealed class ReportsApi(ApiClient api)
{
    public Task<ReportDto> GetAsync(ReportQuery filter) =>
        api.GetAsync<ReportDto>(ApiQuery.Build(ApiRoutes.Reports,
            ("from", filter.From), ("to", filter.To), ("channel", filter.Channel)));
}
