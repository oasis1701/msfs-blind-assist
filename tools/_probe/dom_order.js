(function(){
  if(!window.__MSFSBA_FLYPAD) return 'NO_AGENT';
  var s; try { s = JSON.parse(__MSFSBA_FLYPAD.scrape()); } catch(e){ return 'ERR '+e; }
  var bw = document.body.getBoundingClientRect().width || 1280;
  var mid = bw*0.45;
  var crossings=0, lastSide=null, items=[];
  (s.elements||[]).forEach(function(e){
    if(!e.idx) return;                 // interactive controls only (these carry the order risk)
    var node = document.querySelector('[data-fbw-efb-idx="'+e.idx+'"]');
    if(!node) return;
    var L = node.getBoundingClientRect().left;
    var side = L<mid ? 'L' : 'R';
    if(lastSide!==null && side!==lastSide) crossings++;
    lastSide=side;
    items.push({k:e.kind, t:(e.text||'').slice(0,26), L:Math.round(L), s:side});
  });
  // Count items per side to know if it's genuinely two-column.
  var nL=0,nR=0; items.forEach(function(i){ if(i.s==='L')nL++; else nR++; });
  return JSON.stringify({page:s.page, mid:Math.round(mid), crossings:crossings, leftN:nL, rightN:nR, items:items});
})()
