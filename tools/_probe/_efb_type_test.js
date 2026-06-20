(function () {
  var flds = document.querySelectorAll('.boeing-efb-textfield-button');
  var target = null;
  for (var i = 0; i < flds.length; i++) { var inp = flds[i].querySelector('input'); if (inp && inp.value === '') { target = inp; break; } }
  if (!target) return 'NO-EMPTY-INPUT';
  target.focus();
  try { target.click(); } catch (e) {}
  var txt = '15';
  for (var c = 0; c < txt.length; c++) {
    var ch = txt.charAt(c), code = ch.charCodeAt(0);
    var o = { bubbles: true, cancelable: true, key: ch, keyCode: code, which: code, charCode: code };
    try { target.dispatchEvent(new KeyboardEvent('keydown', o)); } catch (e) {}
    try { target.dispatchEvent(new KeyboardEvent('keypress', o)); } catch (e) {}
    try { target.dispatchEvent(new KeyboardEvent('keyup', o)); } catch (e) {}
  }
  return JSON.stringify({ immediateValue: target.value });
})()
