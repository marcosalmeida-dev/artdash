using ArtDash.Application.Common.Validation;
using ArtDash.Application.Features.Customers;
using ArtDash.Application.Features.Deliveries;
using ArtDash.Shared.Contracts.Customers;
using ArtDash.Shared.Contracts.Deliveries;

namespace ArtDash.Application.Tests;

public sealed class ValidationTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    [InlineData("11.222.333/0001-81")]
    [InlineData("11222333000181")]
    public void Valid_cpf_and_cnpj_are_accepted(string document) =>
        ValidationRules.IsValidCpfOrCnpj(document).ShouldBeTrue();

    [Theory]
    [InlineData("529.982.247-24")]
    [InlineData("111.111.111-11")]
    [InlineData("11.222.333/0001-82")]
    [InlineData("00000000000000")]
    [InlineData("1234")]
    public void Invalid_cpf_and_cnpj_are_rejected(string document) =>
        ValidationRules.IsValidCpfOrCnpj(document).ShouldBeFalse();

    [Fact]
    public void A_complete_customer_is_valid()
    {
        var result = new SaveCustomerRequestValidator().Validate(Customer());

        result.IsValid.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void A_customer_with_a_bad_document_phone_and_zip_code_is_rejected()
    {
        var request = Customer();
        request.Document = "123";
        request.Phone = "9999";
        request.Address.ZipCode = "0131";

        var result = new SaveCustomerRequestValidator().Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.Select(e => e.PropertyName).ShouldContain(nameof(SaveCustomerRequest.Document));
        result.Errors.Select(e => e.PropertyName).ShouldContain(nameof(SaveCustomerRequest.Phone));
        result.Errors.Select(e => e.PropertyName).ShouldContain("Address.ZipCode");
    }

    [Fact]
    public void Payment_terms_are_limited_to_180_days()
    {
        var request = Customer();
        request.PaymentTermDays = 181;

        new SaveCustomerRequestValidator().Validate(request).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("ABC1D23", true)]
    [InlineData("abc-1234", true)]
    [InlineData("AB12", false)]
    [InlineData("", false)]
    public void Vehicle_plates_must_have_seven_characters(string plate, bool valid) =>
        new SaveVehicleRequestValidator().Validate(new SaveVehicleRequest { Plate = plate, Type = VehicleType.Van })
            .IsValid.ShouldBe(valid);

    private static SaveCustomerRequest Customer() => new()
    {
        Type = CustomerType.Company,
        Name = "Mercadinho Bom Preço Ltda",
        Document = "11.222.333/0001-81",
        Email = "compras@bompreco.com.br",
        Phone = "(11) 98888-7777",
        AllowInvoicing = true,
        PaymentTermDays = 28,
        CreditLimit = 5000m,
        Address = new AddressDto
        {
            ZipCode = "04538-133",
            Street = "Avenida Brigadeiro Faria Lima",
            Number = "3477",
            District = "Itaim Bibi",
            City = "São Paulo",
            State = "SP"
        }
    };
}
