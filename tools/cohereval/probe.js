(function(){try{
  var root=document.getElementById("MSFS_REACT_MOUNT");
  var efb=document.getElementById("EFB");
  var rootN=root?root.getElementsByTagName("*").length:-1;
  var efbN=efb?efb.getElementsByTagName("*").length:-1;
  var bodyN=document.body?document.body.getElementsByTagName("*").length:0;
  var samp=[];
  var src=(root&&rootN>0)?root:document.body;
  var els=src.getElementsByTagName("*");
  for(var i=0;i<els.length&&samp.length<18;i++){
    var c=els[i].className&&els[i].className.toString?els[i].className.toString():"";
    if(c)samp.push(els[i].tagName.toLowerCase()+"|"+c.substring(0,55));
  }
  return JSON.stringify({title:document.title,hasReactMount:!!root,reactMountChildren:rootN,hasEFB:!!efb,efbChildren:efbN,bodyChildren:bodyN,sample:samp});
}catch(e){return "ERR "+(e&&e.message?e.message:e);}})()
