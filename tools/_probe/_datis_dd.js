(function(){
  try{
    var A=window.__MSFSBA_A380; var page=A.findRoot(A.activeMcdu); if(!page) return "NO ROOT";
    var nodes=page.querySelectorAll(A.INTERACTIVE_SELECTOR), out=[];
    for(var i=0;i<nodes.length;i++){var n=nodes[i];if(!A.isVisible(n))continue;if(n.querySelector(A.INTERACTIVE_SELECTOR))continue;
      var k=A.classify(n);
      if(k==="dropdown"){
        var hasMsg=!!n.querySelector(".mfd-atccom-datis-block-msgarea");
        var inner=n.querySelector(".mfd-dropdown-inner");
        // parent chain
        var pc=[],p=n;for(var d=0;d<4&&p;d++){pc.push((""+(p.className||"")).slice(0,30));p=p.parentElement;}
        out.push("DD cls="+(""+n.className).slice(0,45)+"\n   text="+A.lineText(n,k).slice(0,55)+"\n   inner="+(inner?"'"+(inner.textContent||"").replace(/\s+/g," ").trim().slice(0,40)+"'":"NONE")+" hasMsgChild="+hasMsg+"\n   chain="+pc.join(" < "));
      }
    }
    return out.join("\n")||"no dropdowns";
  }catch(e){return "ERR "+e;}
})();
