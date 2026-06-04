(function(){
  try {
    var r = JSON.parse(window.__MSFSBA_A380.scrape());
    if (!r.ok) return "scrape not ok: "+(r.error||"");
    var out = (r.title||"?") + "\n";
    for (var i=0;i<r.elements.length;i++){ var e=r.elements[i]; out += "- "+e.kind+" | "+e.text+"\n"; }
    return out;
  } catch(e){ return "ERR "+e; }
})();
