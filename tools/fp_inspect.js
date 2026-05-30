(function () {
  var A = window.__MSFSBA_FLYPAD;
  function cls(n){ try { return (n.className && n.className.toString) ? n.className.toString() : ""; } catch(e){ return ""; } }
  function clean(t){ return (t||"").replace(/\s+/g," ").trim(); }
  function own(n){ var s=""; for(var i=0;i<n.childNodes.length;i++){ if(n.childNodes[i].nodeType===3) s+=n.childNodes[i].nodeValue; } return clean(s); }
  var all = document.getElementsByTagName("*"), out = [], seen = 0;
  for (var i=0;i<all.length && seen<30;i++){
    var n = all[i]; var tag = n.tagName.toLowerCase();
    var isField = tag==="input" || tag==="textarea" || (n.getAttribute && n.getAttribute("contenteditable")==="true");
    if (!isField) continue;
    if (A && A.isVisible && !A.isVisible(n)) continue;
    seen++;
    var lbl = (A && A.labelFor) ? A.labelFor(n) : "(noagent)";
    var line = "#"+seen+" <"+tag+"> val='"+(n.value!=null?n.value:"")+"'  AGENT_LABEL='"+lbl+"'";
    line += "\n   self.cls=["+cls(n).substring(0,75)+"] ph='"+(n.getAttribute("placeholder")||"")+"' aria='"+(n.getAttribute("aria-label")||"")+"'";
    var p=n.parentElement, d=0;
    while(p && d<5){
      line += "\n   ^p"+d+" <"+p.tagName.toLowerCase()+"> own='"+own(p)+"' txt='"+clean(p.textContent).substring(0,45)+"' cls=["+cls(p).substring(0,48)+"]";
      d++; p=p.parentElement;
    }
    var q=n, hops=0, sib="";
    while(q && hops<3){ var ps=q.previousElementSibling; if(ps){ sib += " [p"+hops+":'"+clean(ps.textContent).substring(0,28)+"']"; } q=q.parentElement; hops++; }
    line += "\n   prevSibChain:"+(sib||" none");
    out.push(line);
  }
  return "VISIBLE INPUTS: "+seen+"\n"+out.join("\n\n");
})()
