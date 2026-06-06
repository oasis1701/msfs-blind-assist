// Inspect flyPad Ground-services DOOR tiles: find each tile (a node with a child
// heading whose text contains "Door"/"Cargo"), and report its clickability, color
// state classes, opacity, and the tile element's own class — so we can design the
// open/closed readout.
(function () {
  var A = window.__MSFSBA_FLYPAD;
  function cls(n){ return (n.className&&n.className.toString)?n.className.toString():""; }
  function reactClick(n){ try{var ks=Object.keys(n);for(var i=0;i<ks.length;i++){if(ks[i].indexOf("__reactProps")===0){var p=n[ks[i]];if(p&&typeof p.onClick==="function")return true;}}}catch(e){}return false; }
  var root = A.findRoot();
  var hs = root.querySelectorAll("h1,h2,h3,h4,h5,h6");
  var out = [];
  for (var i=0;i<hs.length;i++){
    var t=(hs[i].textContent||"").replace(/\s+/g," ").trim();
    if (!/door|cargo/i.test(t)) continue;
    // climb to the tile wrapper (a few levels up that carries color/opacity/onclick)
    var node=hs[i], info={ name:t, levels:[] };
    var cur=hs[i].parentElement, g=0;
    while(cur && g<4){
      var c=cls(cur);
      info.levels.push({
        tag:cur.tagName.toLowerCase(),
        onclick: typeof cur.onclick==="function",
        react: reactClick(cur),
        green: c.indexOf("bg-green")>=0 || (cur.querySelector&&!!cur.querySelector('[class*="bg-green"]')),
        amber: c.indexOf("bg-amber")>=0 || (cur.querySelector&&!!cur.querySelector('[class*="bg-amber"]')),
        opacity20: c.indexOf("opacity-20")>=0,
        cls: c.slice(0,90)
      });
      cur=cur.parentElement; g++;
    }
    out.push(info);
  }
  return JSON.stringify(out, null, 1);
})();
