(function () {
  function summarize() {
    var out = { title: document.title, url: location.href };
    out.canvases = document.querySelectorAll('canvas').length;
    out.imgs = document.querySelectorAll('img').length;
    out.svgs = document.querySelectorAll('svg').length;
    var all = document.querySelectorAll('*');
    out.elemCount = all.length;
    var tags = {};
    for (var i = 0; i < all.length; i++) {
      var t = all[i].tagName.toLowerCase();
      tags[t] = (tags[t] || 0) + 1;
    }
    out.tagCounts = tags;
    out.bodyTextLen = document.body ? (document.body.innerText || '').length : 0;
    out.bodyTextSample = document.body ? (document.body.innerText || '').replace(/\s+/g, ' ').slice(0, 400) : '';
    // identify mounted instrument custom-elements (tag names containing a dash)
    var customTags = [];
    for (var k in tags) { if (k.indexOf('-') >= 0) customTags.push(k + 'x' + tags[k]); }
    out.customTags = customTags;
    // window globals that hint at PMDG / instrument data objects
    out.globals = Object.keys(window).filter(function (k) {
      return /instrument|pmdg|gauge|baseinstrument|cdu|fmc|eicas|nd|pfd|mfd|comm|synoptic/i.test(k);
    }).slice(0, 60);
    // Does a BaseInstrument exist and expose anything?
    try {
      if (typeof BaseInstrument !== 'undefined') out.hasBaseInstrument = true;
    } catch (e) {}
    return out;
  }
  try { return JSON.stringify(summarize()); }
  catch (e) { return 'PROBE_ERR ' + e; }
})()
