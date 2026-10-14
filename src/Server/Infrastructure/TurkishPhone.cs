using System.Text.RegularExpressions;

namespace Server.Infrastructure;

public static partial class TurkishPhone
{
    public static bool TryNormalize(string? input, out string normalized, out string error)
    {
        normalized = "";
        error = "Geçerli bir Türkiye telefon numarası girin.";
        var phone = input?.Trim();
        if (phone is not { Length: > 0 and <= 32 } || !PhoneCharacters().IsMatch(phone)) return false;
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        if (phone.StartsWith('+') && (digits.Length != 12 || !digits.StartsWith("90", StringComparison.Ordinal)))
        {
            error = "Uluslararası telefon numarası +90 ile başlamalı.";
            return false;
        }
        if (digits.Length == 11 && digits[0] == '0') digits = digits[1..];
        if (digits.Length == 12 && digits.StartsWith("90", StringComparison.Ordinal)) digits = digits[2..];
        error = "Geçerli bir Türkiye cep veya sabit telefon numarası girin.";
        if (!TurkishNumber().IsMatch(digits)) return false;
        normalized = "+90" + digits;
        error = "";
        return true;
    }

    [GeneratedRegex(@"\A\+?[0-9 ()-]+\z")]
    private static partial Regex PhoneCharacters();
    [GeneratedRegex(@"\A[2345][0-9]{9}\z")]
    private static partial Regex TurkishNumber();
}
