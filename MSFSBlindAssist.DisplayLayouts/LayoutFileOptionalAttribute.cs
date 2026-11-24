namespace MSFSBlindAssist.DisplayLayouts;

/// <summary>
/// Marks a layout record's init-only property that a layout file may lack: one added after files without it were
/// written (an optional part and its reason). <see cref="LayoutFileFormat"/> still saves it, nulls included, but loads
/// a file without it, the property keeping its initializer's value; every other member stays required. The record
/// itself checks that the values it ends up with agree (a part null exactly when its reason is not).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class LayoutFileOptionalAttribute : Attribute;
