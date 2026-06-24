(function(){
  function txt(el){return (el.textContent||'').replace(/\s+/g,' ').trim();}
  // look for EICAS caution/warning/message elements across this view
  var sels = ['[class*="eicas-message"]','[class*="message"]','[class*="caution"]','[class*="warning"]','[class*="advisory"]','[class*="memo"]','[class*="alert"]','[class*="annunc"]','[class*="msg"]'];
  var hits={}, out=[];
  sels.forEach(function(s){ try{ var n=document.querySelectorAll(s); for(var i=0;i<n.length;i++){ var c=(n[i].className||'').toString(); if(!hits[c]){hits[c]=1; var t=txt(n[i]).slice(0,50); out.push('cls="'+c.slice(0,40)+'" text="'+t+'"'); } } }catch(e){} });
  // also try the instrument instance for a messages/alerts member
  var inst=null, all=document.querySelectorAll('*');
  for(var j=0;j<all.length;j++){ if(all[j].fsInstrument){ inst=all[j].fsInstrument; break; } }
  var instMsg=[];
  if(inst){ for(var k in inst){ if(/message|caution|warning|alert|eicas|annunc|memo/i.test(k)) instMsg.push(k); } }
  return 'MSG-ELEMENT CLASSES ('+out.length+'):\n'+out.slice(0,30).join('\n')+'\n\nINSTANCE message members: '+(instMsg.join(', ')||'(none)');
})()
