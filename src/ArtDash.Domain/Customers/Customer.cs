namespace ArtDash.Domain.Customers;

/// <summary>
/// Who receives and pays for orders. Separate from the login (<c>ApplicationUser</c>): a B2B customer
/// ordered by phone may never have an account, and a customer account links back here via <see cref="UserId"/>.
/// </summary>
public sealed class Customer : BaseSoftDeleteTenantEntity
{
    public CustomerType Type { get; set; } = CustomerType.Individual;
    public string Name { get; set; } = string.Empty;

    /// <summary>Nome fantasia (companies).</summary>
    public string? TradeName { get; set; }

    /// <summary>CPF or CNPJ, digits only.</summary>
    public string? Document { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Address Address { get; set; } = new();

    /// <summary>Whether the store accepts invoiced (pay-later) orders from this customer.</summary>
    public bool AllowInvoicing { get; set; }

    /// <summary>Days until an invoiced order is due. 0 uses the tenant default.</summary>
    public int PaymentTermDays { get; set; }

    /// <summary>Maximum open balance on invoiced orders. Null or 0 means no limit.</summary>
    public decimal? CreditLimit { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public Guid? UserId { get; set; }

    /// <summary>Throws when an invoiced order of <paramref name="orderTotal"/> would exceed the credit limit.</summary>
    public void EnsureCanInvoice(decimal orderTotal, decimal currentOpenBalance)
    {
        if (!AllowInvoicing)
        {
            throw new DomainException("Este cliente não está habilitado para pedidos faturados.");
        }

        if (CreditLimit is > 0 && currentOpenBalance + orderTotal > CreditLimit.Value)
        {
            throw new DomainException(
                $"Limite de crédito excedido: em aberto {currentOpenBalance:C} + pedido {orderTotal:C} ultrapassa {CreditLimit.Value:C}.");
        }
    }

    public static string? NormalizeDocument(string? document)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            return null;
        }

        var digits = new string(document.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }
}
