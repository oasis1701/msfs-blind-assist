(function(){try{
  var r=(document.getElementById("MFD_LEFT_PARENT_DIV")||document).querySelector(".mfd-main")||document.body;
  var conts=r.querySelectorAll(".mfd-input-field-container");
  var node=conts[1]; // flight-number field on INIT
  if(!node)return "no field 1";
  var span=node.querySelector(".mfd-input-field-text-input");
  var sd=node.querySelector(".mfd-input-field-text-input-container");
  function dispatchKey(target,type,code){
    var ev;
    try{ev=new KeyboardEvent(type,{bubbles:true,cancelable:true});}catch(e){ev=document.createEvent("Event");ev.initEvent(type,true,true);}
    try{Object.defineProperty(ev,"keyCode",{get:function(){return code;}});}catch(e){}
    try{Object.defineProperty(ev,"which",{get:function(){return code;}});}catch(e){}
    try{Object.defineProperty(ev,"charCode",{get:function(){return code;}});}catch(e){}
    target.dispatchEvent(ev);
  }
  // focus
  try{if(sd.click)sd.click();else sd.dispatchEvent(new MouseEvent("click",{bubbles:true,cancelable:true}));}catch(e){}
  try{if(span.focus)span.focus();}catch(e){}
  try{var fe;try{fe=new FocusEvent("focus",{bubbles:false});}catch(e){fe=document.createEvent("Event");fe.initEvent("focus",false,false);}span.dispatchEvent(fe);}catch(e){}
  var before=span.textContent;
  // type BVI214
  var v="BVI214";
  for(var i=0;i<v.length;i++){dispatchKey(span,"keypress",v.charCodeAt(i));}
  var midway=span.textContent;
  dispatchKey(span,"keypress",13); // ENTER
  return JSON.stringify({before:before,midway:midway,afterEnterImmediate:span.textContent});
}catch(e){return "ERR "+(e&&e.message?e.message:e);}})()
