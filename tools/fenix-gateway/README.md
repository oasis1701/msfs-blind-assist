# `tools/fenix-gateway/`: read the Fenix v3 gateway's own web apps

Two Python scripts for checking what the Fenix A320 family v3 itself sends and shows. `fetch_gateway_bundles.py`
only reads from the pilot's own running Fenix at `http://127.0.0.1:8083` (the gateway listens on IPv4 only) and
writes nothing to the sim; `option_labels.py` never touches the gateway, it reads a chunk file that script saved.
The EFB and FAP bundles are Fenix's code: save them in their own new, empty folder outside the repository and
never commit them. Run each script with `python -I`, so a file in the current folder cannot be imported in its
place.

## `fetch_gateway_bundles.py`

Downloads the EFB and FAP web-app bundles from the running Fenix, so the data names, write calls and GraphQL
operations the app uses can be checked against the source.

```
python -I fetch_gateway_bundles.py <new-empty-output-folder>
```

Needs the sim running with a Fenix A319/A320/A321 v3 loaded. It reads only (HTTP GET). It writes
`<out>/efb/*.js` and `<out>/fap/*.js` (the app scripts and every chunk the runtime lists), `<out>/data-names.txt`
(every quoted data name found) and `<out>/gql-operations.txt` (every query, mutation and subscription text
found), and prints how many of each it found.

## `option_labels.py`

Extracts the Fenix EFB's own option labels (the settings page's text) from the one EFB bundle chunk that defines
the `humanizeKey` pipe. It reads the object literal that ends right before that pipe with a small literal
parser, so a value label stays with its own option and no description is cut short.

```
python -I option_labels.py <chunk.js> <out.tsv>
```

`<chunk.js>` is a bundle saved by `fetch_gateway_bundles.py`: find the file under `<out>/efb/` that contains
`name:"humanizeKey"`. It reads that file and writes `<out.tsv>`, a UTF-8 tab-separated file with a header row
(`key`, `value`, `label`, `description`, `control`): an option row has an empty `value`, an enum value row
repeats the option's key and names the value, and tabs and line breaks inside text become single spaces. It
stops with a message if a label or description holds a `|`, the field separator of the table in
`MSFSBlindAssist/Services/FenixOptionLabels.cs`, which the rows are pasted into.
