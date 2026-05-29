(function(){try{
  function vis(n){try{var st=window.getComputedStyle(n);if(st.display==="none"||st.visibility==="hidden")return false;var r=n.getBoundingClientRect();return r.width>0&&r.height>0;}catch(e){return false;}}
  function cls(n){return (n.className&&n.className.toString)?n.className.toString().toLowerCase():"";}
  function classify(n){var c=cls(n);var role=(n.getAttribute&&n.getAttribute("role")||"").toLowerCase();var tag=n.tagName.toLowerCase();
    if(tag==="a")return "link";if(tag==="input")return "input";if(tag==="select")return "select";if(tag==="textarea")return "input";
    if(/^h[1-6]$/.test(tag))return "heading";if(role==="slider"||c.indexOf("slider")>=0)return "slider";
    if(role==="checkbox"||role==="switch"||c.indexOf("toggle")>=0||c.indexOf("switch")>=0||c.indexOf("checkbox")>=0)return "toggle";
    if(role==="tab"||c.indexOf("tab-")>=0||c.indexOf("-tab")>=0)return "tab";
    if(role==="button"||c.indexOf("button")>=0||c.indexOf("btn")>=0||tag==="button")return "button";
    if(n.getAttribute&&n.getAttribute("contenteditable")==="true")return "input";return null;}
  var root=document.getElementById("MSFS_REACT_MOUNT")||document.body;
  var all=root.getElementsByTagName("*");
  var total=all.length, classified=0, visibleClass=0, anchors=0, buttons=0, headings=0, visAny=0, rectZero=0;
  var sampleVisible=[];
  for(var i=0;i<all.length;i++){var n=all[i];var v=vis(n);if(v)visAny++;else{try{var r=n.getBoundingClientRect();if(r.width===0&&r.height===0)rectZero++;}catch(e){}}
    var k=classify(n);if(k){classified++;if(k==="link")anchors++;if(k==="button")buttons++;if(k==="heading")headings++;if(v){visibleClass++;if(sampleVisible.length<10){var t=(n.textContent||"").replace(/\s+/g," ").substring(0,30);sampleVisible.push(k+":"+t);}}}}
  return JSON.stringify({total:total,classified:classified,visibleClass:visibleClass,visAny:visAny,rectZero:rectZero,anchors:anchors,buttons:buttons,headings:headings,sampleVisible:sampleVisible,agentInstalled:(typeof window.__MSFSBA_FLYPAD!=="undefined")});
}catch(e){return "ERR "+(e&&e.message?e.message:e);}})()
