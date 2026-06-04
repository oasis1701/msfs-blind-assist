(function(){
  try{
    var A=window.__MSFSBA_A380; var page=A.findRoot(A.activeMcdu); if(!page) return "NO ROOT";
    function dtext(el){var s="";for(var c=0;c<el.childNodes.length;c++){if(el.childNodes[c].nodeType===3)s+=el.childNodes[c].nodeValue;}return s.replace(/\s+/g," ").trim();}
    var out=[];
    // dropdowns
    var dd=page.querySelectorAll(".mfd-dropdown-outer, .mfd-dropdown-container, [class*=dropdown]");
    out.push("DROPDOWN-ish ("+dd.length+"):");
    for(var i=0;i<dd.length && i<8;i++){var e=dd[i];if(!A.isVisible(e))continue;
      var inner=e.querySelector(".mfd-dropdown-inner");
      out.push("  cls="+(""+e.className).slice(0,40)+" | tc="+(e.textContent||"").replace(/\s+/g," ").trim().slice(0,50)+" | inner="+(inner?dtext(inner).slice(0,40):"(none)"));}
    // ATIS body text containers
    out.push("ATIS-ish text classes:");
    var all=page.getElementsByTagName("*");
    for(var j=0;j<all.length;j++){var el=all[j];if(!A.isVisible(el))continue;var t=dtext(el);
      if(/ATIS INFO|ARR INFO/.test(t)){out.push("  cls="+(""+el.className).slice(0,50)+" tag="+el.tagName+" len="+t.length);}}
    return out.join("\n");
  }catch(e){return "ERR "+e;}
})();
