namespace NetTrace.Models;

public sealed class NavItem(AppPage page, string label, string glyph)
{
    public AppPage Page { get; } = page;
    public string Label { get; } = label;
    public string Glyph { get; } = glyph;

    /// <summary>used as the accessible name of the navigation entry.</summary>
    public override string ToString() => Label;
}
