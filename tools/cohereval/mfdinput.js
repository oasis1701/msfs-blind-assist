(function(){try{
  function root(){var c=document.getElementById("MFD_LEFT_PARENT_DIV");var m=c?c.querySelector(".mfd-main"):null;if(m)return m;var a=document.querySelectorAll(".mfd-main");return a.length?a[0]:document.body;}
  var r=root();
  var title=[];var tb=r.querySelector(".mfd-title-bar-container");if(tb){var s=tb.querySelectorAll(".mfd-title-bar-text");for(var i=0;i<s.length;i++)title.push(s[i].textContent);}
  var inputs=r.querySelectorAll(".mfd-input-field-container");
  var out=[];
  for(var j=0;j<inputs.length&&j<6;j++){
    var n=inputs[j];
    var inner=n.querySelector(".mfd-input-field-text-input");
    out.push({
      idx:j,
      containerTag:n.tagName.toLowerCase(),
      containerClass:(n.className&&n.className.toString?n.className.toString():"").substring(0,60),
      innerTag:inner?inner.tagName.toLowerCase():"(none)",
      innerText:inner?(inner.textContent||"").substring(0,20):"",
      innerEditable:inner?inner.getAttribute("contenteditable"):null,
      tabindex:n.getAttribute("tabindex"),
      onclickAttr:!!n.getAttribute("onclick"),
      id:n.id||inner&&inner.id||""
    });
  }
  return JSON.stringify({title:title,inputCount:inputs.length,kbdL:(typeof SimVar!=="undefined"?SimVar.GetSimVarValue("L:A32NX_KCCU_L_KBD_ON_OFF","bool"):"noSimVar"),inputs:out});
}catch(e){return "ERR "+(e&&e.message?e.message:e);}})()
