(function () {
  // Close EVERY open SelectInput: the OPTIONS panel is the .absolute.z-10 child
  // (the chevron svg is .absolute too — matching bare .absolute toggles closed
  // dropdowns OPEN; that was the bug in the first probe). Toggle each root shut.
  var closed = [];
  var all = document.querySelectorAll("div");
  for (var i = 0; i < all.length; i++) {
    var n = all[i];
    var c = " " + (n.className || "") + " ";
    if (c.indexOf(" border-theme-accent ") >= 0 && c.indexOf(" cursor-pointer ") >= 0) {
      var panel = n.querySelector(".absolute.z-10");
      if (panel) {
        n.click();
        closed.push((n.textContent || "").replace(/\s+/g, " ").substring(0, 25));
      }
    }
  }
  return closed.length ? "closed " + closed.length + ": " + closed.join(" | ") : "no open SelectInput found";
})()
