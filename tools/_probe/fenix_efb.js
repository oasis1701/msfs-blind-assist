(function () {
  try {
    var out = { title: document.title, url: location.href };
    out.elemCount = document.querySelectorAll('*').length;
    out.bodyTextLen = document.body ? (document.body.innerText || '').length : 0;
    out.iframes = document.querySelectorAll('iframe').length;
    out.scripts = document.querySelectorAll('script').length;
    var tags = {}; var all = document.querySelectorAll('*');
    for (var i = 0; i < all.length; i++) { var t = all[i].tagName.toLowerCase(); tags[t] = (tags[t] || 0) + 1; }
    var custom = {}; for (var k in tags) { if (k.indexOf('-') >= 0) custom[k] = tags[k]; }
    out.customTags = custom;
    // Look for any string anywhere referencing 'aircraft.' dataRef names in inline scripts
    var refs = {};
    var scr = document.querySelectorAll('script');
    for (var i = 0; i < scr.length; i++) {
      var txt = scr[i].textContent || '';
      var m = txt.match(/aircraft\.[a-zA-Z0-9_.]+/g);
      if (m) for (var j = 0; j < m.length; j++) refs[m[j]] = 1;
    }
    out.scriptDataRefs = Object.keys(refs).slice(0, 60);
    out.efbGlobals = Object.keys(window).filter(function (k) { return /efb|fenix|graphql|dataref|apollo|aircraft/i.test(k); }).slice(0, 40);
    return JSON.stringify(out);
  } catch (e) { return 'PROBE_ERR ' + e; }
})()
