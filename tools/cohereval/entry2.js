(function(){try{
  var r=(document.getElementById("MFD_LEFT_PARENT_DIV")||document).querySelector(".mfd-main")||document.body;
  var conts=r.querySelectorAll(".mfd-input-field-container");
  var span=null,idx=-1;
  for(var i=0;i<conts.length;i++){var cl=conts[i].className.toString();if(cl.indexOf("disabled")>=0)continue;var inner=conts[i].querySelector(".mfd-input-field-text-input");if(inner&&inner.textContent.indexOf("▯")>=0){span=inner;idx=i;break;}}
  if(!span)return "no empty field found";
  function press(code){
    var ev;
    try{ev=new KeyboardEvent("keypress",{bubbles:true,cancelable:true});}catch(e){ev=document.createEvent("Event");ev.initEvent("keypress",true,true);}
    try{Object.defineProperty(ev,"keyCode",{get:function(){return code;}});}catch(e){}
    try{Object.defineProperty(ev,"which",{get:function(){return code;}});}catch(e){}
    try{Object.defineProperty(ev,"charCode",{get:function(){return code;}});}catch(e){}
    span.dispatchEvent(ev);
  }
  if(span.click)span.click();else span.dispatchEvent(new MouseEvent("click",{bubbles:true,cancelable:true}));
  var codes=[50,49,52,13]; // "2","1","4",ENTER
  var s=0;function next(){if(s>=codes.length)return;press(codes[s]);s++;setTimeout(next,90);}
  setTimeout(next,180);
  return JSON.stringify({fieldIndex:idx, before:span.textContent});
}catch(e){return "ERR "+(e&&e.message?e.message:e);}})()
