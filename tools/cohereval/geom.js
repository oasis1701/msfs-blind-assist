(function(){try{
  var root=(document.getElementById("MFD_LEFT_PARENT_DIV")||document).querySelector(".mfd-main")||document.body;
  var page=root.querySelector(".mfd-navigator-container")||root;
  var pr=page.getBoundingClientRect();
  function vis(n){try{var s=getComputedStyle(n);if(s.display==="none"||s.visibility==="hidden")return false;var r=n.getBoundingClientRect();return r.width>0&&r.height>0;}catch(e){return true;}}
  function clean(s){return (s||"").replace(/\s+/g," ").replace(/^\s+|\s+$/g,"");}
  var out={labels:[],inputs:[]};
  var labs=page.querySelectorAll(".mfd-label");
  for(var i=0;i<labs.length;i++){if(!vis(labs[i]))continue;var t=clean(labs[i].textContent);if(!t)continue;var r=labs[i].getBoundingClientRect();out.labels.push({t:t,top:Math.round(r.top-pr.top),left:Math.round(r.left-pr.left),right:Math.round(r.right-pr.left),bot:Math.round(r.bottom-pr.top)});}
  var fields=page.querySelectorAll(".mfd-input-field-container");
  for(var j=0;j<fields.length;j++){if(!vis(fields[j]))continue;var sp=fields[j].querySelector(".mfd-input-field-text-input");var r2=fields[j].getBoundingClientRect();out.inputs.push({v:sp?clean(sp.textContent):"",top:Math.round(r2.top-pr.top),left:Math.round(r2.left-pr.left),right:Math.round(r2.right-pr.left),bot:Math.round(r2.bottom-pr.top),disabled:fields[j].className.toString().indexOf("disabled")>=0});}
  return JSON.stringify(out);
}catch(e){return "ERR "+(e&&e.message?e.message:e);}})()
