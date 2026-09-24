using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Resolves a PMDG CDA field name the way IPMDGDataManager.GetFieldValue does - an exact
/// NON-array struct field, or a "Base_N" suffix where Base is a marshalled array and N is
/// inside its SizeConst - and fails otherwise. GetFieldValue returns the 0.0 unknown-field
/// sentinel instead of throwing, so a name that does not resolve ships as a control stuck at
/// "--"/Off or a light that never lights, with only a Log.Debug line to show for it. Two
/// hand-typed literals agreeing with each other cannot catch that; this can.
/// </summary>
internal static class PmdgStructFields
{
    public static void AssertResolves777(string field, string context)
        => AssertResolves(typeof(MSFSBlindAssist.SimConnect.PMDG777XDataStruct), field, context);

    public static void AssertResolves(Type cdaStruct, string field, string context)
    {
        var exact = cdaStruct.GetField(field);
        if (exact != null && !exact.FieldType.IsArray) return;

        int cut = field.LastIndexOf('_');
        if (cut > 0 && int.TryParse(field[(cut + 1)..], out int index))
        {
            var baseField = cdaStruct.GetField(field[..cut]);
            int size = baseField?.GetCustomAttribute<MarshalAsAttribute>()?.SizeConst ?? 0;
            if (baseField != null && baseField.FieldType.IsArray && index < size) return;
        }

        Assert.Fail($"{context}: field '{field}' does not resolve against {cdaStruct.Name} " +
            "- GetFieldValue would return the 0.0 sentinel forever");
    }
}
