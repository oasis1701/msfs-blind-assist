(function () {
  var cdu = document.querySelector('.wt787-cdu');
  if (!cdu) return 'no .wt787-cdu';
  function cnt(sel) { return cdu.querySelectorAll(sel).length; }
  // sample classes of buttons + LSK containers to see the real structure
  var allBtns = cdu.querySelectorAll('.wt787-cdu-button');
  var firstBtnCls = allBtns.length ? (allBtns[0].className + ' | parent=' + (allBtns[0].parentElement && allBtns[0].parentElement.className)) : 'none';
  var leftCol = cdu.querySelector('.wt787-cdu-lsk-column-left');
  var anyLsk = cdu.querySelector('.wt787-cdu-lsk');
  return JSON.stringify({
    buttons: cnt('.wt787-cdu-button'),
    leftCol: cnt('.wt787-cdu-lsk-column-left'),
    rightCol: cnt('.wt787-cdu-lsk-column-right'),
    lsks: cnt('.wt787-cdu-lsk'),
    leftLsks: cnt('.wt787-cdu-lsk-column-left .wt787-cdu-lsk'),
    rightLsks: cnt('.wt787-cdu-lsk-column-right .wt787-cdu-lsk'),
    firstBtnCls: firstBtnCls,
    // alt selectors that might be the real ones:
    altButtons: cdu.querySelectorAll('button').length,
    clickableDivs: cdu.querySelectorAll('[onclick],[data-id],.cdu-button,.button').length,
    leftColHtml: leftCol ? leftCol.outerHTML.slice(0, 300) : 'no leftCol'
  });
})()
