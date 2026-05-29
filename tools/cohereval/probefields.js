(function(){try{
  var r=(document.getElementById("MFD_LEFT_PARENT_DIV")||document).querySelector(".mfd-main")||document.body;
  var conts=r.querySelectorAll(".mfd-input-field-container");
  var out=[];
  for(var i=0;i<conts.length;i++){
    var c=conts[i];
    var sp=c.querySelector(".mfd-input-field-text-input");
    var sd=c.querySelector(".mfd-input-field-text-input-container");
    var rect=c.getBoundingClientRect();
    out.push({i:i,disabled:c.className.toString().indexOf("disabled")>=0,hasSpan:!!sp,hasSpanningDiv:!!sd,text:sp?sp.textContent:"",vis:(rect.width>0&&rect.height>0)});
  }
  return JSON.stringify({count:conts.length, fields:out});
}catch(e){return "ERR "+(e&&e.message?e.message:e);}})()
