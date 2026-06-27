(function () {
  // Close an OPEN SelectInput without changing its value: the open root is the
  // border-theme-accent div that currently contains the absolute options panel —
  // clicking the root toggles it shut.
  var all = document.querySelectorAll("div");
  for (var i = 0; i < all.length; i++) {
    var n = all[i];
    var c = " " + (n.className || "") + " ";
    if (c.indexOf(" border-theme-accent ") >= 0 && c.indexOf(" cursor-pointer ") >= 0 &&
        n.querySelector(".absolute")) {
      n.click();
      return "toggled shut: " + (n.textContent || "").replace(/\s+/g, " ").substring(0, 40);
    }
  }
  return "no open SelectInput found";
})()
