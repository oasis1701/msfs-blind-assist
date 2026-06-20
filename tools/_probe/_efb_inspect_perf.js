(function () {
  function lv(el){ var s=[]; var n=el.querySelectorAll('*'); for(var i=0;i<n.length;i++){ if(n[i].children.length===0){ var t=(n[i].textContent||'').replace(/\s+/g,' ').replace(/^\s+|\s+$/g,''); if(t) s.push(n[i].tagName+':'+t); } } return s; }
  var out = {};
  function grab(needle, key){
    var els = document.querySelectorAll('.boeing-efb-button, .boeing-efb-dropdown-button, .boeing-efb-textfield-button');
    for (var i=0;i<els.length;i++){ var e=els[i]; var t=(e.textContent||'').replace(/\s+/g,' ').replace(/^\s+|\s+$/g,'');
      if (t.indexOf(needle) >= 0){ out[key] = { cls:e.className, textContent:t, leaves:lv(e), html:e.outerHTML.slice(0,500) }; return; } }
    out[key] = 'NOT FOUND';
  }
  grab('ANDBALANCE','wtbalance_button');
  grab('THRUST','thrust_dropdown');
  grab('LIMC','airport_dropdown');
  grab('TOW','tow_input');
  return JSON.stringify(out);
})()
