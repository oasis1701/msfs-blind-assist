(function(){
  function own(el){var t='';for(var i=0;i<el.childNodes.length;i++){var n=el.childNodes[i];if(n.nodeType===3)t+=n.nodeValue;}return t.replace(/\s+/g,' ').trim();}
  function txt(el){return (el.textContent||'').replace(/\s+/g,' ').trim();}
  var tfs=document.querySelectorAll('.boeing-efb-textfield-button'), out=[];
  for(var i=0;i<tfs.length && i<3;i++){ var tf=tfs[i];
    var par=tf.parentElement;
    var sibs=[]; if(par){ for(var j=0;j<par.children.length;j++){ var ch=par.children[j]; if(ch!==tf){ sibs.push(ch.tagName+'.'+((ch.className||'').toString().slice(0,25))+'="'+txt(ch).slice(0,20)+'"'); } } }
    out.push('TF['+i+'] own="'+own(tf)+'" innerTxt="'+txt(tf).slice(0,25)+'"\n  parentCls='+((par&&par.className)||'').toString().slice(0,40)+'\n  siblings: '+sibs.slice(0,5).join(' | '));
  }
  return out.join('\n\n');
})()
