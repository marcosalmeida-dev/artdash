using System.Text.RegularExpressions;

namespace ArtDash.Application.Common.Validation;

public static partial class ValidationRules
{
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();

    public static IRuleBuilderOptions<T, string> HexColor<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Must(value => value is not null && HexColorRegex().IsMatch(value))
            .WithMessage("Cor inválida. Use o formato #RRGGBB.");

    /// <summary>Optional CPF (11 digits) or CNPJ (14 digits) with check digits.</summary>
    public static IRuleBuilderOptions<T, string?> BrazilianDocument<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value => string.IsNullOrWhiteSpace(value) || IsValidCpfOrCnpj(value))
            .WithMessage("CPF/CNPJ inválido.");

    /// <summary>Optional Brazilian phone: 10 to 13 digits (with or without country code).</summary>
    public static IRuleBuilderOptions<T, string?> Phone<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value => string.IsNullOrWhiteSpace(value) || value.Digits().Length is >= 10 and <= 13)
            .WithMessage("Telefone inválido. Informe DDD + número.");

    public static bool IsValidCpfOrCnpj(string value)
    {
        var digits = value.Digits();
        return digits.Length switch
        {
            11 => IsValidCpf(digits),
            14 => IsValidCnpj(digits),
            _ => false
        };
    }

    private static bool IsValidCpf(string cpf)
    {
        if (cpf.Distinct().Count() == 1)
        {
            return false;
        }

        var numbers = cpf.Select(c => c - '0').ToArray();

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += numbers[i] * (10 - i);
        }

        var first = sum % 11 < 2 ? 0 : 11 - (sum % 11);
        if (numbers[9] != first)
        {
            return false;
        }

        sum = 0;
        for (var i = 0; i < 10; i++)
        {
            sum += numbers[i] * (11 - i);
        }

        var second = sum % 11 < 2 ? 0 : 11 - (sum % 11);
        return numbers[10] == second;
    }

    private static bool IsValidCnpj(string cnpj)
    {
        if (cnpj.Distinct().Count() == 1)
        {
            return false;
        }

        int[] firstWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] secondWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        var numbers = cnpj.Select(c => c - '0').ToArray();

        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += numbers[i] * firstWeights[i];
        }

        var first = sum % 11 < 2 ? 0 : 11 - (sum % 11);
        if (numbers[12] != first)
        {
            return false;
        }

        sum = 0;
        for (var i = 0; i < 13; i++)
        {
            sum += numbers[i] * secondWeights[i];
        }

        var second = sum % 11 < 2 ? 0 : 11 - (sum % 11);
        return numbers[13] == second;
    }
}

/// <summary>Validates a delivery / registration address.</summary>
public sealed class AddressDtoValidator : AbstractValidator<AddressDto>
{
    public AddressDtoValidator()
    {
        RuleFor(x => x.ZipCode).Must(zip => zip.Digits().Length == 8).WithMessage("CEP inválido.");
        RuleFor(x => x.Street).NotEmpty().WithName("Rua").MaximumLength(200);
        RuleFor(x => x.Number).NotEmpty().WithName("Número").MaximumLength(20);
        RuleFor(x => x.Complement).MaximumLength(100);
        RuleFor(x => x.District).NotEmpty().WithName("Bairro").MaximumLength(100);
        RuleFor(x => x.City).NotEmpty().WithName("Cidade").MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().WithName("UF").Length(2);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue);
    }
}
