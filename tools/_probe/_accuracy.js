(function(){
  try{
    var all=document.getElementsByTagName("*"), hits=[];
    for(var i=0;i<all.length;i++){
      var dt=(all[i].childNodes&&all[i].childNodes.length)?"":"";
      var t=(all[i].textContent||"").replace(/\s+/g," ").trim();
      // find the smallest elements whose direct text is ACCURACY or a lone FT near it
      var direct="";
      for(var c=0;c<all[i].childNodes.length;c++){if(all[i].childNodes[c].nodeType===3)direct+=all[i].childNodes[c].nodeValue;}
      direct=direct.replace(/\s+/g," ").trim();
      if(direct==="ACCURACY"||direct==="FT"||/^ACCURACY/.test(direct)){
        var r=all[i].getBoundingClientRect();
        hits.push(direct+" | cls="+(all[i].className||"")+" | rect="+Math.round(r.top)+","+Math.round(r.left)+" vis="+(r.width>0&&r.height>0));
      }
    }
    return hits.join("\n")||"none";
  }catch(e){return "ERR "+e;}
})();
