(function(){
  function vis(el){var r=el.getBoundingClientRect();if(r.width<1||r.height<1)return false;var s=getComputedStyle(el);return s.visibility!=='hidden'&&s.display!=='none'&&parseFloat(s.opacity||'1')>0.05;}
  function own(el){ // own (direct) text, not descendants'
    var t=''; for(var i=0;i<el.childNodes.length;i++){ var n=el.childNodes[i]; if(n.nodeType===3) t+=n.nodeValue; } return t.replace(/\s+/g,' ').trim();
  }
  // every visible element that has its OWN text (a leaf-ish text node), with position
  var all=document.querySelectorAll('body *'), out=[], seen={};
  for(var i=0;i<all.length;i++){ var e=all[i]; if(!vis(e))continue; var t=own(e); if(!t||t.length>40)continue;
    var r=e.getBoundingClientRect(); var key=Math.round(r.top)+'|'+Math.round(r.left)+'|'+t;
    if(seen[key])continue; seen[key]=1;
    var c=(e.className||'').toString();
    out.push({y:Math.round(r.top),x:Math.round(r.left),t:t,interactive:(c.indexOf('boeing-efb')>=0?'CTRL':'')});
  }
  out.sort(function(a,b){return a.y-b.y||a.x-b.x;});
  var lines=[]; for(var j=0;j<out.length && j<60;j++){ lines.push((out[j].interactive||'    ')+' "'+out[j].t+'"'); }
  return 'visible text leaves: '+out.length+'\n'+lines.join('\n');
})()
