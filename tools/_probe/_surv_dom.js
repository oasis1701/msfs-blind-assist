(function(){
  try{
    var A=window.__MSFSBA_A380;
    var page=A.findRoot(A.activeMcdu); if(!page) return "NO ROOT";
    function rect(el){var r=el.getBoundingClientRect();return Math.round(r.top)+","+Math.round(r.left)+".."+Math.round(r.right);}
    function dtext(el){var s="";for(var c=0;c<el.childNodes.length;c++){if(el.childNodes[c].nodeType===3)s+=el.childNodes[c].nodeValue;}return s.replace(/\s+/g," ").trim();}
    var out=[];
    // labels
    var labs=page.querySelectorAll(".mfd-surv-label, .mfd-surv-section-label, [class*=surv-label]");
    out.push("LABELS ("+labs.length+"):");
    for(var i=0;i<labs.length;i++){if(!A.isVisible(labs[i]))continue;out.push("  ["+rect(labs[i])+"] '"+dtext(labs[i])+"' cls="+labs[i].className);}
    // interactive controls + classify
    var nodes=page.querySelectorAll(A.INTERACTIVE_SELECTOR);
    out.push("CONTROLS:");
    for(var j=0;j<nodes.length;j++){var n=nodes[j];if(!A.isVisible(n))continue;if(n.querySelector(A.INTERACTIVE_SELECTOR))continue;
      out.push("  ["+rect(n)+"] "+A.classify(n)+" '"+A.lineText(n,A.classify(n))+"' cls="+(n.className||"").toString().slice(0,60));}
    return out.join("\n");
  }catch(e){return "ERR "+e+" @ "+(e.stack||"");}
})();
