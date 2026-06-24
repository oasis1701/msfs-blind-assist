#!/usr/bin/env python3
import sys
DEF = r'C:\Users\franc\Documents\development\msfs-blind-assist\MSFSBlindAssist\Aircraft\HorizonSim787Definition.cs'

g = open('generated.txt', encoding='utf-8').read()
def section(a, b):
    return g.split(a, 1)[1].split(b, 1)[0].strip('\n')
DEFS = section('<<DEFS>>', '<<STRUCT>>')
STRUCT = section('<<STRUCT>>', '<<CONTROLS>>')   # the "..name.." lines for GetPanelStructure
CONTROLS = section('<<CONTROLS>>', '<<END>>')     # the GetPanelDisplayVariables entries

src = open(DEF, encoding='utf-8').read()

# --- 1. GetVariables: insert the var defs after the last (Cutoff/Run) entry ---
anchor1 = (
    '                ValueDescriptions = new Dictionary<double, string> { [0] = "Cutoff", [1] = "Run" }\n'
    '            }\n'
    '        };'
)
repl1 = (
    '                ValueDescriptions = new Dictionary<double, string> { [0] = "Cutoff", [1] = "Run" }\n'
    '            },\n\n'
    '            // ===================================================================\n'
    '            // FLIGHT DATA — read-only L:var telemetry auto-extracted from the WT/HS787\n'
    '            // instrument JS var surface (VNAV / LNAV / glidepath / FADEC / timers / etc.).\n'
    '            // Numeric readouts; exact units + enum decode pending an in-flight pass.\n'
    '            // ===================================================================\n'
    + DEFS + '\n'
    '        };'
)
assert src.count(anchor1) == 1, f'anchor1 count={src.count(anchor1)}'
src = src.replace(anchor1, repl1)

# --- 2. GetPanelStructure: add a "Flight Data" section ---
anchor2 = (
    '            ["Ground Services"] = new List<string>\n'
    '            {\n'
    '                "Doors",\n'
    '                "Services"\n'
    '            }\n'
    '        };'
)
repl2 = (
    '            ["Ground Services"] = new List<string>\n'
    '            {\n'
    '                "Doors",\n'
    '                "Services"\n'
    '            },\n'
    '            ["Flight Data"] = new List<string>\n'
    '            {\n'
    + STRUCT + '\n'
    '            }\n'
    '        };'
)
assert src.count(anchor2) == 1, f'anchor2 count={src.count(anchor2)}'
src = src.replace(anchor2, repl2)

# --- 3. GetPanelDisplayVariables: add the Flight Data sub-panels (read-only display) ---
anchor3 = (
    '            // The ReadSquawkCode hotkey handles on-demand squawk readback.\n'
    '        };'
)
repl3 = (
    '            // The ReadSquawkCode hotkey handles on-demand squawk readback.\n'
    + CONTROLS + '\n'
    '        };'
)
assert src.count(anchor3) == 1, f'anchor3 count={src.count(anchor3)}'
src = src.replace(anchor3, repl3)

open(DEF, 'w', encoding='utf-8').write(src)
print('inserted OK; DEFS lines:', DEFS.count('\n')+1)
