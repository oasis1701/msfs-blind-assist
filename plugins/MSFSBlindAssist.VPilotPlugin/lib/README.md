# `RossCarlson.Vatsim.Vpilot.Plugins.dll`

vPilot's plugin API assembly — the one that defines `IPlugin`, `IBroker` and the
`*EventArgs` types `Plugin.cs` subscribes to. It is **not ours**; it is Ross
Carlson's, and it ships inside every vPilot installation.

| | |
| --- | --- |
| Assembly | `RossCarlson.Vatsim.Vpilot.Plugins, Version=3.12.1.0, Culture=neutral, PublicKeyToken=null` |
| Size | 15,872 bytes |
| SHA-256 | `E098A921CEDFA9CDAB8A133587F7F4172083C2EE5C870DB2E5F8BF6E42AE1FAE` |
| Copied from | `%LOCALAPPDATA%\vPilot\RossCarlson.Vatsim.Vpilot.Plugins.dll` of a vPilot 3.12.1 install (byte-identical) |

## Why it is committed

`MSFSBlindAssist.VPilotPlugin.csproj` references it by `HintPath`, so the plugin
project cannot compile without it — on a fresh clone or on CI, where no vPilot is
installed. `.gitignore` therefore carries an explicit exception for this one file
(search it for `RossCarlson`).

## Why it never ships

The reference is `<Private>False</Private>`, so it is **not** copied to our build
output and can never land in vPilot's `Plugins` folder. vPilot supplies the real
one at run time, from its own folder, and a stray copy of ours sitting beside our
plugin could shadow it.

## Refreshing it

Only when the plugin needs an API member this version doesn't have — a newer
vPilot loads an older-referenced plugin fine, so there is no routine update:

1. Copy the file from a current vPilot install (`%LOCALAPPDATA%\vPilot\`).
2. Update the table above — version, size and SHA-256 (`Get-FileHash <path> -Algorithm SHA256`).
3. `dotnet build MSFSBlindAssist.sln -c Debug`, and re-run the in-sim VATSIM
   checks in `docs/vatsim.md` — nothing in the automated suite exercises this
   assembly, since the net10 test project cannot reference a net48 one.
