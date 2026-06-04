(function(){
  try{
    var A=window.__MSFSBA_A380; var page=A.findRoot(A.activeMcdu); if(!page) return "NO ROOT";
    var ids=page.querySelectorAll(".mfd-fms-fpln-line-ident");
    for(var i=0;i<ids.length;i++){ if(/HOLTZ/.test((ids[i].textContent||""))){ ids[i].click(); return "clicked HOLTZ revision"; } }
    return "HOLTZ not found ("+ids.length+" idents)";
  }catch(e){return "ERR "+e;}
})();
