// A minimal valid .wasm file built by hand, so the layout store's tests need no real module file: the header, a name section
// naming one function, and one active data segment at a given address. The function's name is part of the code hash
// (every section but the data counts); the segment is what a layout's data ranges read.

using System.Text;

namespace MSFSBlindAssist.Tests;

internal static class TinyWasmFile
{
    /// <summary>The file's bytes: the wasm header, a custom "name" section whose function-names subsection names
    /// function 0 <paramref name="functionName"/>, and a data section holding one active segment of
    /// <paramref name="data"/> at <paramref name="address"/> (an i32.const offset in memory 0).</summary>
    public static byte[] Build(string functionName, uint address, byte[] data)
    {
        var file = new List<byte> { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 };

        var functionNames = new List<byte>();
        U32(functionNames, 1); // one entry
        U32(functionNames, 0); // function 0
        Name(functionNames, functionName);
        var names = new List<byte>();
        Name(names, "name");
        names.Add(1); // the function-names subsection
        U32(names, (uint)functionNames.Count);
        names.AddRange(functionNames);
        Section(file, 0, names);

        var segments = new List<byte>();
        U32(segments, 1); // one segment
        U32(segments, 0); // active, memory 0
        segments.Add(0x41); // i32.const
        S32(segments, unchecked((int)address));
        segments.Add(0x0B); // end
        U32(segments, (uint)data.Length);
        segments.AddRange(data);
        Section(file, 11, segments);

        return file.ToArray();
    }

    private static void Section(List<byte> file, byte id, List<byte> contents)
    {
        file.Add(id);
        U32(file, (uint)contents.Count);
        file.AddRange(contents);
    }

    private static void Name(List<byte> into, string name)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        U32(into, (uint)bytes.Length);
        into.AddRange(bytes);
    }

    private static void U32(List<byte> into, uint value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) b |= 0x80;
            into.Add(b);
        }
        while (value != 0);
    }

    private static void S32(List<byte> into, int value)
    {
        while (true)
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            bool done = (value == 0 && (b & 0x40) == 0) || (value == -1 && (b & 0x40) != 0);
            if (!done) b |= 0x80;
            into.Add(b);
            if (done) return;
        }
    }
}
