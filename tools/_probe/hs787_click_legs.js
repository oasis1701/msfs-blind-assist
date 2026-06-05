// WRITE TEST 2 — DOM-click a CDU page button (the bridge's clickPageButton path)
// from the debugger. Clicks LEGS (page-button index 7, per the bridge map).
(function () {
  var cdu = document.querySelector('.wt787-cdu');
  if (!cdu) return 'NO_CDU';
  var allBtns = cdu.querySelectorAll('.wt787-cdu-button');
  var pageBtns = [];
  for (var i = 0; i < allBtns.length; i++) {
    var b = allBtns[i];
    if (!(b.closest('.wt787-cdu-lsk-column-left') || b.closest('.wt787-cdu-lsk-column-right'))) pageBtns.push(b);
  }
  var legs = pageBtns[7];
  if (!legs) return 'NO_LEGS_BTN (pageBtns=' + pageBtns.length + ')';
  var o = { bubbles: true, cancelable: true };
  try { legs.dispatchEvent(new PointerEvent('pointerdown', o)); } catch (e) {}
  try { legs.dispatchEvent(new PointerEvent('pointerup', o)); } catch (e) {}
  try { legs.dispatchEvent(new MouseEvent('click', o)); } catch (e) {}
  return 'clicked pageBtn[7] (LEGS)';
})()
