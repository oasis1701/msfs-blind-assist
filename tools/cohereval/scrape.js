(function(){try{
  if(typeof window.__MSFSBA_FLYPAD==="undefined") return "NO_AGENT";
  var raw=window.__MSFSBA_FLYPAD.scrape();
  var o=JSON.parse(raw);
  return JSON.stringify({ok:o.ok,error:o.error,page:o.page,count:(o.elements?o.elements.length:-1),first:(o.elements?o.elements.slice(0,8):[])});
}catch(e){return "ERR "+(e&&e.message?e.message:e);}})()
