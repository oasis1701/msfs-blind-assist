(function(){
  function txt(el){ return (el.textContent||'').replace(/\s+/g,' ').trim(); }
  function up(el,n){ var p=el; for(var i=0;i<n&&p;i++) p=p.parentElement; return p; }
  // Find every "checkbox box" candidate: a small bordered square in a checklist row.
  var boxes = Array.prototype.slice.call(document.querySelectorAll('[class*="border-4"]'));
  var rows = boxes.map(function(b){
    // climb to the row that carries the item label text
    var row = b;
    for (var i=0;i<4 && row;i++){ if ((row.className||'').indexOf('flex-row')>=0) break; row = row.parentElement; }
    return { boxClass: (b.className||'').slice(0,90),
             rowClass: row?(row.className||'').slice(0,70):'',
             text: row?txt(row).slice(0,50):txt(up(b,2)||b).slice(0,50),
             hasBorderCurrent: ((b.className||'').indexOf('border-current')>=0)?1:0,
             agentIdx: row?row.getAttribute('data-fbw-efb-idx'):null };
  });
  // Also count rows matched by the AGENT's exact signature.
  var agentMatch = Array.prototype.slice.call(document.querySelectorAll('.space-x-4.flex-row'))
                   .filter(function(r){ try { return !!r.querySelector('.border-4.border-current'); } catch(e){ return false; } });
  return JSON.stringify({ totalBorder4Boxes: boxes.length, agentSignatureMatches: agentMatch.length, rows: rows });
})()
