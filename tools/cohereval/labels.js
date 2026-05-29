(function(){try{
  var r=(document.getElementById("MFD_LEFT_PARENT_DIV")||document).querySelector(".mfd-main")||document.body;
  var conts=r.querySelectorAll(".mfd-input-field-container");
  var out=[];
  for(var i=0;i<conts.length;i++){
    var c=conts[i];
    var sp=c.querySelector(".mfd-input-field-text-input");
    // nearest preceding label
    var lbl="";
    var p=c.previousElementSibling;
    if(p&&p.className&&p.className.toString().indexOf("mfd-label")>=0)lbl=p.textContent;
    // also climb: parent's text minus field
    var par=c.parentElement;
    var parTxt=par?par.textContent:"";
    out.push({i:i,label:lbl.replace(/\s+/g," "),val:sp?sp.textContent:"",par:(parTxt||"").replace(/\s+/g," ").substring(0,40),fmt:sp?(sp.className||""):""});
  }
  return JSON.stringify(out);
}catch(e){return "ERR "+(e&&e.message?e.message:e);}})()
