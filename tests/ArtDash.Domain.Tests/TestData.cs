using ArtDash.Domain.Customers;
using ArtDash.Domain.Deliveries;
using ArtDash.Domain.Orders;

namespace ArtDash.Domain.Tests;

/// <summary>Small builders so each test states only what it is about.</summary>
internal static class TestData
{
    public static readonly DateTime Now = new(2026, 10, 21, 13, 0, 0, DateTimeKind.Utc);

    public static Address Address() => new()
    {
        ZipCode = "01310100",
        Street = "Avenida Paulista",
        Number = "1000",
        District = "Bela Vista",
        City = "São Paulo",
        State = "SP",
        Latitude = -23.5653,
        Longitude = -46.6519
    };

    public static Customer Customer(bool invoicing = false, decimal? creditLimit = null) => new()
    {
        Name = "Mercadinho Bom Preço",
        Type = CustomerType.Company,
        Phone = "11988887777",
        Address = Address(),
        AllowInvoicing = invoicing,
        PaymentTermDays = 28,
        CreditLimit = creditLimit
    };

    /// <summary>An order with one line of 2 × R$ 50,00 (subtotal R$ 100,00) and no delivery fee.</summary>
    public static Order Order(PaymentTiming timing = PaymentTiming.OnDelivery, decimal fee = 0m)
    {
        var order = Orders.Order.Create(
            "000001", TrackingCodeGenerator.New(), Customer(invoicing: true), OrderChannel.Backoffice, timing,
            PaymentMethod.Pix, Address(), null, Now, "teste");

        order.AddItem(Guid.NewGuid(), "Produto", "SKU-1", UnitOfMeasure.Unit, 50m, 2);
        order.SetDeliveryFee(fee);

        if (timing == PaymentTiming.Invoiced)
        {
            order.SetDueDate(DateOnly.FromDateTime(Now).AddDays(28));
        }

        order.EnsureReadyToPlace();
        return order;
    }

    /// <summary>A confirmed order (prepaid ones are paid first).</summary>
    public static Order ConfirmedOrder(PaymentTiming timing = PaymentTiming.OnDelivery)
    {
        var order = Order(timing);
        if (timing == PaymentTiming.Prepaid)
        {
            order.RegisterPayment(PaymentMethod.Pix, order.Total, Now, null, "gateway");
        }
        else
        {
            order.Confirm(Now, "teste");
        }

        return order;
    }

    public static Driver Driver() => new() { UserId = Guid.NewGuid(), Name = "Carlos" };

    public static Vehicle Vehicle() => new() { Plate = "ABC1D23", Type = VehicleType.Van, CapacityKg = 1500m };
}
