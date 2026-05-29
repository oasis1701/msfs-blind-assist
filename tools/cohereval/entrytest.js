(function(){try{
  var r=(document.getElementById("MFD_LEFT_PARENT_DIV")||document).querySelector(".mfd-main")||document.body;
  var conts=r.querySelectorAll(".mfd-input-field-container");
  var target=null,ti=-1;
  for(var i=0;i<conts.length;i++){var cl=conts[i].className.toString();if(cl.indexOf("disabled")>=0)continue;var inner=conts[i].querySelector(".mfd-input-field-text-input");var tx=inner?inner.textContent:"";if(tx.indexOf("▯")>=0){target=conts[i];ti=i;break;}}
  if(!target)return "no target field";
  var inner0=target.querySelector(".mfd-input-field-text-input");
  var before=inner0?inner0.textContent:"";
  function fire(k){try{Coherent.trigger("H:A32NX_KCCU_L_"+k);}catch(e){}try{SimVar.SetSimVarValue("H:A32NX_KCCU_L_"+k,"number",0);}catch(e){}}
  if(target.click)target.click();else target.dispatchEvent(new MouseEvent("click",{bubbles:true,cancelable:true}));
  var focusedClass = target.className.toString();
  var keys=["2","1","4","ENT"];var s=0;
  function next(){if(s>=keys.length)return;fire(keys[s]);s++;setTimeout(next,90);}
  setTimeout(next,150);
  return JSON.stringify({targetIndex:ti, before:before, classAfterClick:focusedClass.substring(0,70)});
}catch(e){return "ERR "+(e&&e.message?e.message:e);}})()
