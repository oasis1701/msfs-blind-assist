(function(){
  try{
    var A=window.__MSFSBA_A380; var page=A.findRoot(A.activeMcdu); if(!page) return "NO ROOT";
    function dtext(el){var s="";for(var c=0;c<el.childNodes.length;c++){if(el.childNodes[c].nodeType===3)s+=el.childNodes[c].nodeValue;}return s.replace(/\s+/g," ").trim();}
    var all=page.getElementsByTagName("*"),out=[];
    for(var i=0;i<all.length;i++){var el=all[i];if(!A.isVisible(el))continue;var t=dtext(el);
      if(/^(WXR DISPLAY 1|WXR DISPLAY OFF|TERR SYS 1|TERR SYS OFF|TAWS|XPDR|TCAS|XPDR 1|XPDR OFF)$/.test(t)){
        out.push("'"+t+"' cls="+(el.className||"")+" isLabel="+((""+el.className).indexOf("mfd-label")>=0 && (""+el.className).indexOf("mfd-label-unit")<0));}}
    return out.join("\n")||"none";
  }catch(e){return "ERR "+e;}
})();
