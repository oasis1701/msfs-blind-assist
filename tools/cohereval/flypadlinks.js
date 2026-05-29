(function(){try{
  var root=document.getElementById("MSFS_REACT_MOUNT")||document.body;
  var links=root.getElementsByTagName("a");
  var out=[];
  for(var i=0;i<links.length&&out.length<20;i++){
    var n=links[i];
    var txt=(n.textContent||"").replace(/\s+/g," ").trim().substring(0,30);
    var aria=n.getAttribute("aria-label")||"";
    var title=n.getAttribute("title")||"";
    var href=n.getAttribute("href")||"";
    // child with aria/title
    var childLabeled=n.querySelector("[aria-label],[title]");
    var childLbl=childLabeled?(childLabeled.getAttribute("aria-label")||childLabeled.getAttribute("title")||""):"";
    // svg <title>
    var svgTitle=n.querySelector("svg title");
    var svgT=svgTitle?svgTitle.textContent:"";
    // nearest preceding heading text (walk DOM order)
    var heading="";
    var p=n;var guard=0;
    while(p&&guard<60){guard++;var prev=p.previousElementSibling;while(prev){if(/^h[1-6]$/i.test(prev.tagName)){heading=(prev.textContent||"").substring(0,25);break;}var h=prev.querySelector&&prev.querySelector("h1,h2,h3,h4");if(h){heading=(h.textContent||"").substring(0,25);break;}prev=prev.previousElementSibling;}if(heading)break;p=p.parentElement;}
    out.push({i:i,txt:txt,aria:aria,title:title,href:href,childLbl:childLbl,svgT:svgT,nearHeading:heading});
  }
  return JSON.stringify(out);
}catch(e){return "ERR "+(e&&e.message?e.message:e);}})()
