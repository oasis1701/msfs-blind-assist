(function(){
  // find the EFB custom element + its instrument instance (A380 fsInstrument pattern)
  var tags = ['hsb789-efb','a380x-efb','efb-instrument','b787-efb'];
  var found = null, tag = '';
  var all = document.querySelectorAll('*');
  for(var i=0;i<all.length;i++){ var t=all[i].tagName.toLowerCase(); if(t.indexOf('efb')>=0 && all[i].fsInstrument){ found=all[i]; tag=t; break; } }
  if(!found){ // fallback: any element with fsInstrument
    for(var j=0;j<all.length;j++){ if(all[j].fsInstrument){ found=all[j]; tag=all[j].tagName.toLowerCase(); break; } }
  }
  if(!found) return 'NO element with fsInstrument found';
  var inst = found.fsInstrument;
  var keys = []; for(var k in inst){ keys.push(k); }
  // look for bus + page-related members
  var busKeys = inst.bus ? 'HAS bus' : 'no bus';
  return JSON.stringify({ tag: tag, instrumentKeys: keys.slice(0,40), bus: busKeys }, null, 1);
})()
