(function () {
  var el = document.querySelector('wtb78x-efb');
  if (!el || !el.fsInstrument) return 'NO-INSTANCE';
  var inst = el.fsInstrument;
  function keysOf(o){ var k=[]; if(!o) return k; for(var p in o){ try{ var t=typeof o[p]; k.push(p+':'+t); }catch(e){ k.push(p+':?'); } } return k; }
  var out = { instKeys: [], perfish: {} };
  // top-level instrument keys (look for takeoffCalculator / performance / perf)
  for (var p in inst) {
    if (/perf|takeoff|calc|wind|oat|weight|balance|tow|zfw/i.test(p)) out.instKeys.push(p + ':' + (typeof inst[p]));
  }
  // probe a few likely model objects
  var cands = ['takeoffCalculator','takeoffPerformance','performanceCalculator','perfData','takeoffData'];
  for (var i=0;i<cands.length;i++){ if(inst[cands[i]]) out.perfish[cands[i]] = keysOf(inst[cands[i]]).slice(0,40); }
  return JSON.stringify(out);
})()
