using System.Globalization;

namespace NetTrace.ViewModels;

/// <summary>formatting helpers callable from x:Bind function bindings.</summary>
public static class Format
{
    public static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
