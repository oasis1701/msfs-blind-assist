(function () {
  // Dump the DOM around the first "Set Active" button: tag/class chain upward and
  // the card's h2 inventory, so the label prefix walks the REAL structure.
  var btns = document.querySelectorAll("button");
  var target = null;
  for (var i = 0; i < btns.length; i++) {
    if ((btns[i].textContent || "").indexOf("Set Active") >= 0 && (btns[i].textContent || "").length < 30) { target = btns[i]; break; }
  }
  if (!target) {
    // maybe not <button> elements — search any node with exact text
    var all = document.querySelectorAll("div,span,h1,h2,h3,a");
    for (var j = 0; j < all.length; j++) {
      var t = (all[j].textContent || "").replace(/\s+/g, " ").trim();
      if (t === "Set Active") { target = all[j]; break; }
    }
  }
  if (!target) return "no Set Active node found";
  var out = ["target: <" + target.tagName + "> class=" + (target.className || ""), "own text: " + target.textContent.replace(/\s+/g, " ").trim()];
  var cur = target.parentElement, hop = 0;
  while (cur && hop < 7) {
    var h2s = cur.querySelectorAll("h2");
    var h2txt = [];
    for (var k = 0; k < Math.min(h2s.length, 4); k++) h2txt.push("'" + h2s[k].textContent.replace(/\s+/g, " ").trim().substring(0, 30) + "'");
    out.push("up" + hop + ": <" + cur.tagName + "> class=" + String(cur.className || "").substring(0, 70) + " | h2s(" + h2s.length + "): " + h2txt.join(", "));
    cur = cur.parentElement; hop++;
  }
  return out.join("\n");
})()
