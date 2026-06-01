// Raw DOM dump of the throttle-calibration page: every visible leaf-text node and
// interactive control with its position + class, sorted (top,left) so the true
// visual grid can be reconstructed. Independent of the agent's read-order logic.
(function(){
  function clean(s){ return (s||'').replace(/\s+/g,' ').replace(/^\s+|\s+$/g,''); }
  function cls(n){ return (n.className && n.className.toString)? n.className.toString():''; }
  function vis(n){ try{ var st=getComputedStyle(n); if(st.display==='none'||st.visibility==='hidden') return false; var r=n.getBoundingClientRect(); return r.width>0&&r.height>0; }catch(e){ return false; } }
  function directText(n){ var s=''; for(var c=0;c<n.childNodes.length;c++){ if(n.childNodes[c].nodeType===3) s+=n.childNodes[c].nodeValue; } return clean(s); }
  var root=document.getElementById('MSFS_REACT_MOUNT')||document.body;
  var all=root.getElementsByTagName('*');
  var out=[];
  for(var i=0;i<all.length;i++){
    var n=all[i]; if(!vis(n)) continue;
    var tag=n.tagName.toLowerCase();
    var dt=directText(n);
    var isInput = tag==='input'||tag==='select'||tag==='textarea';
    if(!dt && !isInput) continue;       // only leaf-text nodes + form fields
    if(dt && dt.length>40) continue;     // skip big container concatenations
    var r=n.getBoundingClientRect();
    out.push({
      top:Math.round(r.top), left:Math.round(r.left), w:Math.round(r.width),
      tag:tag,
      t: isInput ? ('['+tag+' val='+(n.value!=null?n.value:'')+' ph='+(n.getAttribute&&n.getAttribute('placeholder')||'')+']') : dt,
      c: cls(n).slice(0,55)
    });
  }
  out.sort(function(a,b){ return (a.top-b.top)||(a.left-b.left); });
  // Restrict to the calibration content band (skip far-left nav rail x<120)
  var lines=out.filter(function(o){ return o.left>=120; }).map(function(o){
    return o.top+'	'+o.left+'	'+o.tag+'	"'+o.t+'"	{'+o.c+'}';
  });
  return lines.join('\n');
})()
