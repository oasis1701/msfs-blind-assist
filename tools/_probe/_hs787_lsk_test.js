(function(){
  var a=window.__MSFSBA_HS787; var cdu=a&&a.cduRoot&&a.cduRoot(); if(!cdu) return 'no root';
  // structure: list left + right LSK buttons with their text
  function col(side){
    var c = cdu.querySelector('.wt787-cdu-lsk-column-'+side);
    if(!c) return ['(no '+side+' col)'];
    var lsks = c.querySelectorAll('.wt787-cdu-lsk'); var out=[];
    for(var i=0;i<lsks.length;i++){ out.push((i+1)+':"'+((lsks[i].textContent||'').replace(/\s+/g,' ').trim().slice(0,18))+'"'); }
    return out;
  }
  return JSON.stringify({ left: col('left'), right: col('right') });
})()
