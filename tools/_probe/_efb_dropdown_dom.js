(function () {
  function vis(el){ if(!el||!el.getBoundingClientRect) return false; var r=el.getBoundingClientRect(); if(r.width<2||r.height<2) return false; var s=el.ownerDocument.defaultView.getComputedStyle(el); return s.visibility!=='hidden'&&s.display!=='none'&&parseFloat(s.opacity||'1')>0.05; }
  var out = { itemsContainers: [], optionParents: [] };
  // find visible .dropdown-items containers and describe their structure
  var conts = document.querySelectorAll('[class*="dropdown-items"]');
  for (var i=0;i<conts.length;i++){ var c=conts[i]; if(!vis(c)) continue;
    out.itemsContainers.push({
      cls: c.className, tag: c.tagName, parentCls: c.parentElement?c.parentElement.className:'',
      grandparentCls: (c.parentElement&&c.parentElement.parentElement)?c.parentElement.parentElement.className:'',
      prevSibText: c.previousElementSibling?((c.previousElementSibling.textContent||'').slice(0,25)+' ['+c.previousElementSibling.className+']'):'',
      optionCount: c.querySelectorAll('.boeing-efb-button').length
    });
  }
  // for an option button, show its ancestry to the nearest dropdown-button
  var opts = document.querySelectorAll('[class*="dropdown-items"] .boeing-efb-button');
  for (var j=0;j<opts.length && out.optionParents.length<3;j++){ var o=opts[j]; if(!vis(o)) continue;
    var chain=[]; var p=o; for(var k=0;k<6&&p;k++){ chain.push(p.tagName+'.'+(p.className||'').slice(0,40)); p=p.parentElement; }
    out.optionParents.push({ text:(o.textContent||'').slice(0,20), chain:chain });
  }
  return JSON.stringify(out);
})()
