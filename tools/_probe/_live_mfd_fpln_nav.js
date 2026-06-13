(function () {
  var nav = __MSFSBA_A380.navigateUri("fms/active/f-pln");
  var lines = document.querySelectorAll(".mfd-fms-fpln-line").length;
  return "navigateUri -> " + nav + " | fpln lines now: " + lines;
})()
