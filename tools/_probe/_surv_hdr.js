(function(){
  try{
    var A=window.__MSFSBA_A380;
    var page=A.findRoot(A.activeMcdu); if(!page) return "NO ROOT";
    function dtext(el){var s="";for(var c=0;c<el.childNodes.length;c++){if(el.childNodes[c].nodeType===3)s+=el.childNodes[c].nodeValue;}return s.replace(/\s+/g," ").trim();}
    var all=page.getElementsByTagName("*"),out=[];
    var want={"XPDR":1,"TCAS":1,"ALT RPTG":1,"ELEVN/TILT":1,"TAWS":1};
    for(var i=0;i<all.length;i++){var el=all[i];if(!A.isVisible(el))continue;var t=dtext(el);if(want[t]){var r=el.getBoundingClientRect();out.push("'"+t+"' ["+Math.round(r.top)+","+Math.round(r.left)+".."+Math.round(r.right)+"] cls="+(el.className||""));}}
    return out.join("\n")||"none";
  }catch(e){return "ERR "+e;}
})();
