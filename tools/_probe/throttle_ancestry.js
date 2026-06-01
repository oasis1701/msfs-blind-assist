// For each key calibration node (Set from Throttle, the 0.95/1.00 range values,
// Current Value, the Deadband input), walk UP the DOM and report the ancestor
// chain's classes + whether each ancestor contains an "Axis N" heading. Goal:
// find the panel container that distinguishes Axis 1 from Axis 2, and whether the
// shared detent selector lives inside or outside those panels.
(function(){
  function cls(n){ return (n&&n.className&&n.className.toString)? n.className.toString():''; }
  function clean(s){ return (s||'').replace(/\s+/g,' ').trim(); }
  function axisHeadingIn(n){
    try{ var hs=n.querySelectorAll('h1');
      for(var i=0;i<hs.length;i++){ var t=clean(hs[i].textContent); if(/^Axis \d/.test(t)) return t; } }catch(e){}
    return '';
  }
  function chain(node, label){
    var out=[label+':'];
    var p=node, g=0;
    while(p && g<8){
      var ah=axisHeadingIn(p);
      out.push('  ['+g+'] <'+p.tagName.toLowerCase()+'> {'+cls(p).slice(0,50)+'}'+(ah?('  <<contains "'+ah+'">>'):''));
      p=p.parentElement; g++;
    }
    return out.join('\n');
  }
  function findText(txt, tag){
    var all=document.getElementsByTagName('*');
    var hits=[];
    for(var i=0;i<all.length;i++){
      var n=all[i];
      if(tag && n.tagName.toLowerCase()!==tag) continue;
      var dt=''; for(var c=0;c<n.childNodes.length;c++){ if(n.childNodes[c].nodeType===3) dt+=n.childNodes[c].nodeValue; }
      if(clean(dt)===txt) hits.push(n);
    }
    return hits;
  }
  var res=[];
  var sft=findText('Set from Throttle','button');
  for(var i=0;i<sft.length;i++) res.push(chain(sft[i],'Set from Throttle #'+i+' (x='+Math.round(sft[i].getBoundingClientRect().left)+')'));
  var lo=findText('0.95',null);
  for(var j=0;j<lo.length;j++) res.push(chain(lo[j],'0.95 #'+j+' (x='+Math.round(lo[j].getBoundingClientRect().left)+')'));
  var det=findText('TO/GA','span');
  for(var k=0;k<det.length;k++) res.push(chain(det[k],'TO/GA #'+k+' (x='+Math.round(det[k].getBoundingClientRect().left)+')'));
  return res.join('\n\n');
})()
