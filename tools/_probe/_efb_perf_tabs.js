(function () {
  function vis(el){ if(!el||!el.getBoundingClientRect) return false; var r=el.getBoundingClientRect(); if(r.width<2||r.height<2) return false; var s=el.ownerDocument.defaultView.getComputedStyle(el); return s.visibility!=='hidden'&&s.display!=='none'&&parseFloat(s.opacity||'1')>0.05; }
  var out = { titles: [], tabish: [], showLanding: null };
  // all efb-title nodes (visible + hidden) to see takeoff/landing page identity
  var ts = document.querySelectorAll('[class*="efb-title"]');
  for (var i=0;i<ts.length;i++){ out.titles.push({ t:(ts[i].textContent||'').replace(/\s+/g,' ').replace(/^\s+|\s+$/g,''), vis:vis(ts[i]) }); }
  // anything that looks like a tab / takeoff / landing toggle
  var all = document.querySelectorAll('div,span,button');
  for (var j=0;j<all.length && out.tabish.length<40;j++){ var e=all[j]; if(!vis(e)) continue;
    var t=(e.textContent||'').replace(/\s+/g,' ').replace(/^\s+|\s+$/g,'');
    if(/TAKEOFF|LANDING|TAKE-OFF|T\.O\.|TAB/i.test(t) && t.length<30 && e.children.length<=2){
      out.tabish.push({ t:t, cls:e.className, tag:e.tagName });
    }
  }
  return JSON.stringify(out);
})()
