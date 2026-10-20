#!/bin/bash
# runall.sh <outdir> [modes] : over the 100 busiest airports, 6 processes each.
#   modes (default "all wrongturn departures"): any of all | wrongturn | departures
#   VP_PROCS=n  number of parallel processes (default 6)
#   VP_EXE=path  fly a frozen build instead of building the current tree (before/after runs)
set -e
cd "$(dirname "$0")"
O=runs/$1; rm -rf "$O"; mkdir -p "$O"
MODES=${2:-"all wrongturn departures"}
if [ -z "$VP_EXE" ]; then
  # Build first: the harness is NOT in the solution, so a solution build leaves a stale binary.
  dotnet build -c Release -p:Platform=x64 -v q > /dev/null || { echo "VirtualPilot build failed"; exit 1; }
  E=bin/x64/Release/net10.0-windows/VirtualPilot.exe
else E=$VP_EXE; fi
export VP_OSMDIR=${VP_OSMDIR:-$(pwd)/runs/_osm}
for m in $MODES; do
  K=10; [ "$m" = wrongturn ] && K=20
  N=${VP_PROCS:-6}
  for i in $(seq 0 $((N-1))); do $E $m --airports 100 --taxi-per-airport $K --shard $i/$N --out $O/${m}_$i.txt > $O/log_${m}_$i.txt 2>&1 & done; wait
done
cat $O/*_?.txt | grep -v "^#" | awk 'NF{print $1}' | sort | uniq -c
