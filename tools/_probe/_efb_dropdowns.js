(function(){
  function txt(el){return (el.textContent||'').replace(/\s+/g,' ').trim();}
  var dds=document.querySelectorAll('.boeing-efb-dropdown-button'), out=[];
  for(var i=0;i<dds.length;i++){ var d=dds[i]; var r=d.getBoundingClientRect(); if(r.width<2||r.height<2)continue;
    // is there a .button-name sibling in the parent? (label) vs the dropdown's own text (value)
    var par=d.parentElement; var pcls=par?(par.className||'').toString().slice(0,30):'';
    var sibName=par?par.querySelector('.button-name,[class*="button-name"]'):null;
    out.push({y:Math.round(r.top),x:Math.round(r.left),own:txt(d).slice(0,16),parent:pcls,sibLabel:sibName?txt(sibName).slice(0,16):''});
  }
  out.sort(function(a,b){return a.y-b.y||a.x-b.x;});
  var lines=[]; for(var j=0;j<out.length;j++){ lines.push('y='+out[j].y+' x='+out[j].x+' own="'+out[j].own+'" sib="'+out[j].sibLabel+'" par='+out[j].parent); }
  return lines.join('\n');
})()
