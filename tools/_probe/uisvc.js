(function(){ try {
  var mfd=document.querySelector('a380x-mfd'); if(!mfd||!mfd.fsInstrument) return 'no mfd';
  var fi=mfd.fsInstrument;
  var out='fsInstrument keys: '+Object.keys(fi).slice(0,30).join(',');
  var u=fi.uiService||(fi.mfdCaptRef&&fi.mfdCaptRef.instance&&fi.mfdCaptRef.instance.uiService);
  if(u){ out+='\nuiService methods: '+Object.getOwnPropertyNames(Object.getPrototypeOf(u)||{}).slice(0,20).join(','); out+='\nactiveUri='+(u.activeUri&&u.activeUri.get?JSON.stringify(u.activeUri.get()):'?'); }
  else out+='\nno uiService directly; mfdCaptRef='+(fi.mfdCaptRef?'yes':'no');
  return out;
} catch(e){ return 'ERR '+e; } })()
