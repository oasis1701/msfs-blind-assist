(function(){
  try{
    var A=window.__MSFSBA_A380;
    var r=JSON.parse(A.scrape()); if(!r.ok) return "scrape fail";
    for(var i=0;i<r.elements.length;i++){var e=r.elements[i];
      if(/MAG WIND/i.test(e.text) && e.idx>0){ var before=e.text; A.sendToField(e.idx,""); return "cleared idx "+e.idx+" (was: "+before+")"; }}
    return "MAG WIND not found";
  }catch(e){return "ERR "+e;}
})();
