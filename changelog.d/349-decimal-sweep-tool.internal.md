A developer tool, `tools/DecimalSweep`, lists the numbers the app turns into text in the PC's
regional format with no format specifier (`$"{value}"`, `value.ToString()`), which the CI text
scan cannot see: it reads the source with the C# compiler's types. The tree is kept at
"0 to review". `docs/tooling.md` and the tool's own README say how to run it.
