(function () {
  function vis(el){var r=el.getBoundingClientRect();if(r.width<2||r.height<2)return false;var s=getComputedStyle(el);return s.visibility!=='hidden'&&s.display!=='none'&&parseFloat(s.opacity||'1')>0.05;}
  function lbl(el){return (el.getAttribute('aria-label')||el.getAttribute('title')||el.textContent||el.value||'').replace(/\s+/g,' ').trim();}
  // characterize the EFB: clickable elements, inputs, headings, and a sample of the page
  var clickSel='button,[role=button],[onclick],.button,.btn,[class*="button"],[class*="btn"]';
  var clicks=[],seen={};
  var nodes=document.querySelectorAll(clickSel);
  for(var i=0;i<nodes.length;i++){var el=nodes[i];if(!vis(el))continue;var L=lbl(el);if(!L||L.length>45)continue;if(seen['c'+L])continue;seen['c'+L]=1;clicks.push(L);if(clicks.length>=40)break;}
  var inputs=[];
  var ins=document.querySelectorAll('input,textarea,select');
  for(var j=0;j<ins.length;j++){if(!vis(ins[j]))continue;inputs.push((ins[j].tagName.toLowerCase())+':'+(ins[j].getAttribute('placeholder')||ins[j].value||lbl(ins[j])||'?').slice(0,30));}
  // class fingerprint of the most common interactive-ish classes (to know what selectors to scrape)
  var cls={};
  document.querySelectorAll('*').forEach(function(e){(e.className&&e.className.split?e.className.split(' '):[]).forEach(function(c){if(c&&(c.indexOf('btn')>=0||c.indexOf('button')>=0||c.indexOf('menu')>=0||c.indexOf('page')>=0||c.indexOf('tab')>=0))cls[c]=(cls[c]||0)+1;});});
  var topcls=Object.keys(cls).sort(function(a,b){return cls[b]-cls[a];}).slice(0,15).map(function(k){return k+'('+cls[k]+')';});
  return JSON.stringify({clickable:clicks.length,clicks:clicks,inputs:inputs,topInteractiveClasses:topcls},null,1);
})()
