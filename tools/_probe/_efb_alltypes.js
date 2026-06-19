(function(){
  function vis(el){var r=el.getBoundingClientRect();if(r.width<1||r.height<1)return false;var s=getComputedStyle(el);return s.visibility!=='hidden'&&s.display!=='none'&&parseFloat(s.opacity||'1')>0.05;}
  function txt(el){return (el.textContent||'').replace(/\s+/g,' ').trim();}
  var inv = {};
  function bump(k){ inv[k]=(inv[k]||0)+1; }
  // inventory every VISIBLE element by what kind of control/content it is
  var all=document.querySelectorAll('body *'), samples={};
  for(var i=0;i<all.length;i++){ var e=all[i]; if(!vis(e))continue;
    var tag=e.tagName.toLowerCase(); var c=(e.className||'').toString();
    var kind=null;
    if(tag==='input'){ kind='input:'+(e.getAttribute('type')||'text'); }
    else if(tag==='textarea') kind='textarea';
    else if(tag==='select') kind='select';
    else if(tag==='a') kind='link';
    else if(/^h[1-6]$/.test(tag)) kind='heading:'+tag;
    else if(c.indexOf('rc-slider')>=0||c.indexOf('slider')>=0) kind='slider';
    else if(c.indexOf('checkbox')>=0||c.indexOf('check-box')>=0) kind='checkbox';
    else if(c.indexOf('toggle')>=0) kind='toggle';
    else if(c.indexOf('boeing-efb-dropdown')>=0) kind='efb-dropdown';
    else if(c.indexOf('boeing-efb-textfield')>=0) kind='efb-textfield';
    else if(c.indexOf('boeing-efb-button')>=0) kind='efb-button';
    else if(c.indexOf('efb-title')>=0||c.indexOf('title')>=0||c.indexOf('header')>=0||c.indexOf('label')>=0) kind='label/title';
    if(kind){ bump(kind); if(!samples[kind]){ samples[kind]=txt(e).slice(0,30)+' [cls='+c.slice(0,30)+']'; } }
  }
  var out=['PAGE: '+(function(){var t=document.querySelector('[class*="efb-title"]');return t?txt(t):'?';})()];
  for(var k in inv){ out.push(k+' x'+inv[k]+'  e.g. '+(samples[k]||'')); }
  return out.join('\n');
})()
