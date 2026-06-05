(function(){
  var sh=document.querySelector('systems-host');var f=sh&&sh.fwsCore;
  if(!f)return '';
  function lst(key){try{var s=f[key];if(s==null)return[];var v=(typeof s.get==='function')?s.get():(typeof s.getArray==='function'?s.getArray():s);if(v==null)return[];if(Array.isArray(v))return v;if(typeof v.size==='number'&&typeof v.forEach==='function'){var o=[];v.forEach(function(val,k){o.push(k);});return o;}if(typeof v==='object')return Object.keys(v);return[];}catch(e){return[];}}
  function uni(){var set={};for(var i=0;i<arguments.length;i++){var a=lst(arguments[i]);if(a&&a.length)for(var j=0;j<a.length;j++)set[a[j]]=1;}return Object.keys(set);}
  return JSON.stringify({failures:uni('presentedFailures','presentedAbnormalProceduresList'),nonSensed:uni('activeAbnormalNonSensedKeys'),deferred:uni('activeDeferredProceduresList'),inop:uni('inopSysAllPhasesKeys','inopSysApprLdgKeys','inopSysRedundLossKeys'),limits:uni('limitationsAllPhasesKeys','limitationsApprLdgKeys')});
})()
