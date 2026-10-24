namespace ArtDash.Shared.Enums;

/// <summary>Filter for the accounts-receivable list (invoiced orders).</summary>
public enum ReceivableFilter
{
    Open = 1,
    Overdue = 2,
    Paid = 3,
    All = 4
}
