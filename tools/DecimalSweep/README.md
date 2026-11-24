# DecimalSweep

Finds numbers the shipped code turns into text in the PC's culture with **no format specifier**: `$"{value}"`, `value.ToString()`, `"text" + value`. On a German or Swedish Windows such a number reads "228,5" or "2,5 miles", and one written into a web address or a calculator string stops working.

Rule: [CORE-21](../../docs/invariants/core.md#core-21). The CI test `DecimalFormatSourceTests` already scans the source text for a decimal *format* with no invariant call around it (`{value:F3}`), but a text scan cannot know that `{value}` is a `double`. This tool can: it compiles the source in memory with the C# compiler and asks it for each expression's type.

## What it needs

- The **.NET 10 SDK** (the same one the app builds with).
- **The app built once in this checkout** (`dotnet build MSFSBlindAssist.sln -c Debug`). The sweep reads the app's dependencies from `MSFSBlindAssist\bin\x64\<configuration>\net10.0-windows`, and the implicit usings from `obj`. It links no app source and references no app project, so it builds without the MSFS SDK.
- The NuGet package **Microsoft.CodeAnalysis.CSharp** (the compiler as a library, 5.12.0), restored from nuget.org on the first build. Keep it at a version that understands the app's `LangVersion`.

No simulator, no debugger.

## Running it

```powershell
dotnet run --project tools/DecimalSweep -c Release
```

It sweeps the four shipped projects (the app, `MSFSBlindAssist.DisplayLayouts`, the updater, the vPilot plugin: the same four `DecimalFormatSourceTests` scans) in a few seconds.

| Option | What it adds |
|---|---|
| `--all` | Also lists what it leaves out by default: numbers in a log call, in an invariant call, and whole numbers. |
| `--rpn` | Also lists whole numbers written into calculator code in the PC's culture (see below). |
| `--repo <folder>` | The repository root, when not run from inside it. |

Exit code: 0 with nothing to review, 1 with something listed, 2 when it could not run (no build output, no .NET reference assemblies).

## Reading the output

A finding looks like this (one from the tree's first sweep, since wrapped):

```
== MSFSBlindAssist: 1192 files, 5 compile errors
MSFSBlindAssist/Services/VisualGuidanceManager.cs:1547: hole (speech)
    string distanceText = roundedDistance == 1.0 ? "1 mile" : $"{roundedDistance} miles";

1 to review; left out: 64 in a log call, 35 in an invariant call, 10 whole.
```

Each line is a `double`, `float` or `decimal` that becomes text with no format:

- `hole`: an interpolation hole.
- `ToString()`: `value.ToString()` or `value?.ToString()` with no arguments.
- `concat`: `"text" + value` or `text += value`.
- `String.Join`, `StringBuilder.Append` and the like: the value (or a list of them) handed to a method that writes it with no culture.

The words in brackets say what surrounds it: `speech` (an `Announce…` call), `calculator code`, `exception message`.

For each line, decide whether the value can carry a fraction. If it can, wrap the string in `FormattableString.Invariant(...)` or give the number `ToString(CultureInfo.InvariantCulture)`. If it never can, wrap it anyway or round it where it is written (`{Math.Round(nm)}`): the tree is kept at **0 to review**, so a new line always means a new number.

Left out by default, because the answer is already known:

- **In a log call** (`Log.Debug`, a `LogChannel`, `RolloutDiag`, a delegate named `log`): logs may follow the PC's culture.
- **In an invariant call**: `FormattableString.Invariant`, a local `Inv`/`Invariant` helper, or any call given an `IFormatProvider`.
- **Whole**: `Math.Round(x)`, `Floor`, `Ceiling`, `Truncate` with no digits, or a whole constant. No culture gives a whole `double` a separator.

About five compile errors on the app and nine on the vPilot plugin are expected (source-generator output and vPilot's own assemblies are not there); they do not disturb the types. Many more means the app has not been built here.

## `--rpn`: whole numbers in calculator code

A whole number has no separator, but a **negative** one still follows the PC's culture: sv-SE, nb-NO and fi-FI write the minus as U+2212, which the simulator's RPN parser rejects. `--rpn` lists every signed whole number interpolated into a string that looks like calculator code (`(>L:`, `(>K:`, `(L:`, `(A:`) outside an invariant call. Most are switch positions and sequence numbers that are never negative; wrap the ones that can be (a vertical speed, a generic L:var write). `FcuValueEntry.VerticalSpeedSetCode` is the worked example.

## What it cannot see

- A number held as `object` or in a generic type parameter.
- A format string held in a variable (`value.ToString(fmt)`): the text scan cannot see it either. `SpokenDecimalCultureTests` covers the aircraft's display values, announcements and system-display rows by running them under de-DE.
- Code under `#if DEBUG`: the sweep reads what ships.
