(function () {
  function vis(el){ if(!el||!el.getBoundingClientRect) return false; var r=el.getBoundingClientRect(); if(r.width<2||r.height<2) return false; var s=el.ownerDocument.defaultView.getComputedStyle(el); return s.visibility!=='hidden'&&s.display!=='none'&&parseFloat(s.opacity||'1')>0.05; }
  function col(el){ try { return el.ownerDocument.defaultView.getComputedStyle(el).color; } catch(e){ return ''; } }
  var out = { buttons: [], mNodes: [], doorContainers: [] };
  // door action buttons
  var btns = document.querySelectorAll('.boeing-efb-button');
  for (var i=0;i<btns.length;i++){ var b=btns[i]; if(!vis(b)) continue;
    var t=(b.textContent||'').replace(/\s+/g,' ').replace(/^\s+|\s+$/g,'');
    out.buttons.push({ t:t, cls:b.className, color:col(b), parentCls:b.parentElement?b.parentElement.className:'', prevSib:b.previousElementSibling?((b.previousElementSibling.textContent||'').slice(0,20)+' ['+b.previousElementSibling.className+']'):'', nextSib:b.nextElementSibling?((b.nextElementSibling.textContent||'').slice(0,20)+' ['+b.nextElementSibling.className+']'):'' });
  }
  // any element whose own text is exactly "M"
  var all = document.querySelectorAll('body *');
  for (var j=0;j<all.length;j++){ var e=all[j]; if(!vis(e)) continue;
    var ot=''; for(var k=0;k<e.childNodes.length;k++){ if(e.childNodes[k].nodeType===3) ot+=e.childNodes[k].nodeValue; }
    ot=ot.replace(/\s+/g,' ').replace(/^\s+|\s+$/g,'');
    if(ot==='M'){ out.mNodes.push({ cls:e.className, tag:e.tagName, parentCls:e.parentElement?e.parentElement.className:'', color:col(e) }); }
  }
  // look for a doors diagram/container with door-state classes
  var cands = document.querySelectorAll('[class*="door"],[class*="Door"]');
  for (var m=0;m<cands.length && out.doorContainers.length<25;m++){ var c=cands[m]; if(!vis(c)) continue;
    out.doorContainers.push({ cls:c.className, tag:c.tagName, color:col(c), txt:(c.textContent||'').replace(/\s+/g,' ').slice(0,30) });
  }
  return JSON.stringify(out);
})()
