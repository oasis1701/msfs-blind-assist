(function(){try{
  function root(){var c=document.getElementById("MFD_LEFT_PARENT_DIV");var m=c?c.querySelector(".mfd-main"):null;if(m)return m;var a=document.querySelectorAll(".mfd-main");return a.length?a[0]:document.body;}
  var r=root();
  // Dump any element whose class mentions title/header, with text.
  var out=[];
  var all=r.getElementsByTagName("*");
  for(var i=0;i<all.length&&out.length<25;i++){
    var n=all[i];var c=(n.className&&n.className.toString)?n.className.toString():"";
    if(/title|header|page-?selector|breadcrumb|sys/i.test(c)){
      var t=(n.textContent||"").replace(/\s+/g," ").substring(0,40);
      if(t)out.push({cls:c.substring(0,55),tag:n.tagName.toLowerCase(),text:t});
    }
  }
  // Also the active page URI simvar
  var pageL=(typeof SimVar!=="undefined")?SimVar.GetSimVarValue("L:A380X_MFD_L_ACTIVE_PAGE","string"):"n/a";
  return JSON.stringify({activePageSimvar:pageL,titleish:out});
}catch(e){return "ERR "+(e&&e.message?e.message:e);}})()
