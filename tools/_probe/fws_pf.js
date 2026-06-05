(function(){
  var sh=document.querySelector('systems-host');var f=sh&&sh.fwsCore;
  if(!f)return 'no fwsCore';
  function lst(k){try{var s=f[k];if(s==null)return[];var v=(typeof s.get==='function')?s.get():s;if(v==null)return[];if(Array.isArray(v))return v;if(typeof v.forEach==='function'){var o=[];v.forEach(function(val,key){o.push(key);});return o;}return Object.keys(v);}catch(e){return 'ERR';}}
  return JSON.stringify({presentedFailures:lst('presentedFailures'),presentedAbn:lst('presentedAbnormalProceduresList')});
})()
