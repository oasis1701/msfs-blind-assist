(function () {
  function vis(el){ if(!el||!el.getBoundingClientRect) return false; var r=el.getBoundingClientRect(); if(r.width<2||r.height<2) return false; var s=el.ownerDocument.defaultView.getComputedStyle(el); return s.visibility!=='hidden'&&s.display!=='none'; }
  var out = [];
  var flds = document.querySelectorAll('.boeing-efb-textfield-button');
  for (var i=0;i<flds.length && out.length<6;i++){ var f=flds[i]; if(!vis(f)) continue;
    var inp = f.querySelector('input,textarea');
    var txt = (f.textContent||'').replace(/\s+/g,' ').replace(/^\s+|\s+$/g,'');
    out.push({
      label: txt.slice(0,30),
      cls: f.className,
      hasInput: !!inp,
      inputType: inp ? inp.type : null,
      inputValue: inp ? inp.value : null,
      inputReadOnly: inp ? inp.readOnly : null,
      reactKeys: (function(){ var k=[]; for(var p in f){ if(p.indexOf('__react')===0) k.push(p.slice(0,22)); } if(inp){ for(var q in inp){ if(q.indexOf('__react')===0) k.push('inp:'+q.slice(0,18)); } } return k; })(),
      innerHtml: f.innerHTML.slice(0, 260)
    });
  }
  return JSON.stringify(out);
})()
