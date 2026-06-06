// For each in-flight (disabled) door tile, probe whether the door enum is still
// reachable via the React FIBER's memoizedProps.onClick (even though the DOM onClick
// is undefined when disabled). doorIdentity parses `/* Main1Left */` from the
// const-enum-inlined closure source.
(function () {
  var A = window.__MSFSBA_FLYPAD;
  function cls(n){ return (n.className&&n.className.toString)?n.className.toString():""; }
  function fiberOf(n){ try{var ks=Object.keys(n);for(var i=0;i<ks.length;i++){if(ks[i].indexOf("__reactFiber")===0)return n[ks[i]];}}catch(e){}return null; }
  function propsOf(n){ try{var ks=Object.keys(n);for(var i=0;i<ks.length;i++){if(ks[i].indexOf("__reactProps")===0)return n[ks[i]];}}catch(e){}return null; }
  function enumFrom(fn){ if(typeof fn!=="function")return ""; var m=/\/\*\s*([A-Za-z0-9]+)\s*\*\//.exec(""+fn); return m?m[1]:""; }
  var root = A.findRoot();
  var hs = root.querySelectorAll("h1,h2,h3,h4,h5,h6");
  var out = [];
  for (var i=0;i<hs.length;i++){
    var t=(hs[i].textContent||"").replace(/\s+/g," ").trim();
    if (!/door|cargo/i.test(t)) continue;
    // climb to the cursor-pointer tile wrapper
    var tile=hs[i], g=0; while(tile&&g<5){ if(cls(tile).indexOf("cursor-pointer")>=0)break; tile=tile.parentElement; g++; }
    var info={ label:t, domOnclick: typeof (tile&&tile.onclick)==="function" };
    // DOM-element react props onClick (conditional; undefined when disabled)
    var p=tile?propsOf(tile):null; info.propsOnClickEnum = p?enumFrom(p.onClick):"";
    // walk the FIBER chain up a few levels, checking memoizedProps.onClick
    var fb=tile?fiberOf(tile):null, hops=0, fiberEnum="";
    while(fb&&hops<8){
      var mp=fb.memoizedProps;
      if(mp&&typeof mp.onClick==="function"){ var e=enumFrom(mp.onClick); if(e){ fiberEnum=e; break; } }
      fb=fb.return; hops++;
    }
    info.fiberOnClickEnum=fiberEnum; info.fiberHops=hops;
    out.push(info);
  }
  return JSON.stringify(out, null, 1);
})();
