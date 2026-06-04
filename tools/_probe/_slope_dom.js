(function(){
  try{
    var A=window.__MSFSBA_A380,p=A.findRoot(A.activeMcdu);if(!p)return"NO ROOT";
    function dt(el){var s="";for(var c=0;c<el.childNodes.length;c++){if(el.childNodes[c].nodeType===3)s+=el.childNodes[c].nodeValue;}return s.replace(/\s+/g," ").trim();}
    var all=p.getElementsByTagName("*"),out=[];
    for(var i=0;i<all.length;i++){var el=all[i];if(!A.isVisible(el))continue;var t=dt(el);
      if(t==="SLOPE"||t==="-3.0"||t==="°"||/^-?\d+\.\d$/.test(t)&&t.length<6){var r=el.getBoundingClientRect();out.push("'"+t+"' top="+Math.round(r.top)+" L="+Math.round(r.left)+".."+Math.round(r.right)+" cls="+(""+el.className).slice(0,40));}}
    return out.join("\n")||"none";
  }catch(e){return"ERR "+e;}
})();
