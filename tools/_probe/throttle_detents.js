// Inspect the detent selector buttons: full className of each, its parent and
// grandparent classNames (selection highlight likely lives on an ancestor), and
// any aria. Also dump the low/high value nodes' ancestry to see how a detent's
// range is structurally tied to the detent (for labeling).
(function(){
  function cls(n){ return (n&&n.className&&n.className.toString)? n.className.toString():''; }
  function clean(s){ return (s||'').replace(/\s+/g,' ').trim(); }
  var all=document.getElementsByTagName('*');
  var detentNames=['TO/GA','FLX','CLB','Idle','Reverse Idle','Reverse Full'];
  var out=[];
  for(var i=0;i<all.length;i++){
    var n=all[i];
    var dt=''; for(var c=0;c<n.childNodes.length;c++){ if(n.childNodes[c].nodeType===3) dt+=n.childNodes[c].nodeValue; }
    dt=clean(dt);
    if(detentNames.indexOf(dt)>=0 && n.tagName.toLowerCase()==='span'){
      var p=n.parentElement, gp=p&&p.parentElement;
      out.push('DETENT "'+dt+'"'
        +'\n   self: '+cls(n)
        +'\n   parent<'+(p?p.tagName.toLowerCase():'')+'>: '+cls(p)
        +'\n   gp<'+(gp?gp.tagName.toLowerCase():'')+'>: '+cls(gp)
        +'\n   aria-selected='+(n.getAttribute('aria-selected'))+' role='+(n.getAttribute('role')));
    }
  }
  // value nodes
  for(var j=0;j<all.length;j++){
    var m=all[j];
    var d2=''; for(var c2=0;c2<m.childNodes.length;c2++){ if(m.childNodes[c2].nodeType===3) d2+=m.childNodes[c2].nodeValue; }
    d2=clean(d2);
    if(d2==='0.95'||d2==='1.00'||d2.indexOf('Current Value')===0){
      var pp=m.parentElement;
      out.push('VALUE "'+d2+'" cls={'+cls(m)+'} parentText="'+clean(pp?pp.textContent:'').slice(0,40)+'" parentCls={'+cls(pp)+'}');
    }
  }
  return out.join('\n');
})()
