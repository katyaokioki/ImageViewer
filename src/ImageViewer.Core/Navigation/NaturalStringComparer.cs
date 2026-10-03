namespace ImageViewer.Core.Navigation;

/// <summary>
/// «Естественное» сравнение имён файлов: img2 &lt; img10 (в отличие от обычного лексикографического).
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                // Пропускаем ведущие нули, затем сравниваем числа сначала по длине, потом по цифрам
                int si = i, sj = j;
                while (si < x.Length && x[si] == '0') si++;
                while (sj < y.Length && y[sj] == '0') sj++;

                int ei = si, ej = sj;
                while (ei < x.Length && char.IsAsciiDigit(x[ei])) ei++;
                while (ej < y.Length && char.IsAsciiDigit(y[ej])) ej++;

                int byLength = (ei - si).CompareTo(ej - sj);
                if (byLength != 0) return byLength;

                int byDigits = string.CompareOrdinal(x, si, y, sj, ei - si);
                if (byDigits != 0) return Math.Sign(byDigits);

                i = ei;
                j = ej;
            }
            else
            {
                int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (c != 0) return Math.Sign(c);
                i++;
                j++;
            }
        }

        int rest = (x.Length - i).CompareTo(y.Length - j);
        return rest != 0 ? rest : Math.Sign(string.CompareOrdinal(x, y));
    }
}
