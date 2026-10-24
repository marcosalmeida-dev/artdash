using ArtDash.Domain.Customers;
using ArtDash.Domain.Deliveries;
using ArtDash.Domain.Tenants;

namespace ArtDash.Domain.Tests;

public sealed class TenantAndRecordsTests
{
    [Theory]
    [InlineData(" Acme ", "acme")]
    [InlineData("Pão-Quente", "po-quente")]
    [InlineData("loja_123", "loja123")]
    public void Tenant_slugs_are_lower_case_letters_digits_and_hyphens(string input, string expected) =>
        Tenant.NormalizeSlug(input).ShouldBe(expected);

    [Theory]
    [InlineData("a")]
    [InlineData("")]
    public void Tenant_slugs_need_at_least_two_characters(string input) =>
        Should.Throw<DomainException>(() => Tenant.NormalizeSlug(input));

    [Fact]
    public void A_tenant_host_is_stored_once_without_port()
    {
        var tenant = new Tenant("Acme", "acme");

        tenant.AddDomain("Pedidos.Acme.com.br:443");
        tenant.AddDomain("pedidos.acme.com.br");

        tenant.Domains.Single().Host.ShouldBe("pedidos.acme.com.br");
    }

    [Theory]
    [InlineData(100, 12)]
    [InlineData(249.99, 12)]
    [InlineData(250, 0)]
    public void Delivery_is_free_above_the_threshold(decimal subtotal, decimal expectedFee)
    {
        var store = new TenantStoreSettings { DeliveryFee = 12m, FreeDeliveryThreshold = 250m };

        store.DeliveryFeeFor(subtotal).ShouldBe(expectedFee);
    }

    [Fact]
    public void Without_a_threshold_the_flat_fee_always_applies()
    {
        var store = new TenantStoreSettings { DeliveryFee = 8m };

        store.DeliveryFeeFor(10_000m).ShouldBe(8m);
    }

    [Theory]
    [InlineData("abc-1d23", "ABC1D23")]
    [InlineData("ABC 1234", "ABC1234")]
    public void Plates_are_normalized(string input, string expected) =>
        Vehicle.NormalizePlate(input).ShouldBe(expected);

    [Fact]
    public void Zip_codes_keep_only_digits() =>
        Address.NormalizeZipCode("01310-100").ShouldBe("01310100");

    [Fact]
    public void An_address_reads_as_one_line()
    {
        var address = TestData.Address();
        address.Complement = "Sala 12";

        address.ToSingleLine().ShouldBe("Avenida Paulista, 1000 - Sala 12 • Bela Vista • São Paulo/SP");
    }

    [Fact]
    public void Invoicing_respects_the_customer_flag_and_credit_limit()
    {
        Should.Throw<DomainException>(() => TestData.Customer(invoicing: false).EnsureCanInvoice(10m, 0m));

        var limited = TestData.Customer(invoicing: true, creditLimit: 1000m);
        Should.NotThrow(() => limited.EnsureCanInvoice(400m, 600m));
        Should.Throw<DomainException>(() => limited.EnsureCanInvoice(400.01m, 600m));

        var unlimited = TestData.Customer(invoicing: true, creditLimit: null);
        Should.NotThrow(() => unlimited.EnsureCanInvoice(1_000_000m, 1_000_000m));
    }

    [Theory]
    [InlineData("123.456.789-09", "12345678909")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void Documents_keep_only_digits(string? input, string? expected) =>
        Customer.NormalizeDocument(input).ShouldBe(expected);
}
