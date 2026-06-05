(function(){
  var sh=document.querySelector('systems-host');var f=sh&&sh.fwsCore;
  if(!f)return 'no fwsCore';
  function lst(k){try{var s=f[k];if(s==null)return null;var v=(typeof s.get==='function')?s.get():(typeof s.getArray==='function'?s.getArray():s);if(v==null)return null;if(Array.isArray(v))return v;if(typeof v.size==='number'&&typeof v.forEach==='function'){var o=[];v.forEach(function(val,key){o.push(key);});return o;}if(typeof v==='object')return Object.keys(v);return v;}catch(e){return 'ERR';}}
  var cats=['presentedFailures','allCurrentFailures','recallFailures','presentedAbnormalProceduresList','activeAbnormalNonSensedKeys','clearedAbnormalProceduresList','activeDeferredProceduresList','memos','inopSys','limitations','pfdMemoLines','pfdLimitationsLines','limitationsAllPhasesKeys','limitationsApprLdgKeys','inopSysAllPhasesKeys','inopSysApprLdgKeys','inopSysRedundLossKeys','statusNormal','ecamStatusNormal'];
  var out={};
  for(var i=0;i<cats.length;i++){var v=lst(cats[i]);if(v!==null)out[cats[i]]=v;}
  return JSON.stringify(out);
})()
