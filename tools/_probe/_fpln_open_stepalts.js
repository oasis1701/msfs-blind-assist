(function(){
  try{
    var A=window.__MSFSBA_A380; var page=A.findRoot(A.activeMcdu); if(!page) return "NO ROOT";
    var items=page.querySelectorAll(".mfd-context-menu-element, .mfd-dropdown-menu-element");
    for(var i=0;i<items.length;i++){ if(/STEP ALT/i.test((items[i].textContent||"")) && (items[i].textContent||"").indexOf("N/A")<0){ items[i].click(); return "clicked STEP ALTs"; } }
    return "STEP ALTs item not found ("+items.length+" menu items)";
  }catch(e){return "ERR "+e;}
})();
