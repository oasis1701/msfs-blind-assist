(function () {
  if (!window.__MSFSBA_HS787) return 'AGENT MISSING';
  var s = window.__MSFSBA_HS787.scrape();
  if (!s.visible) return 'CDU NOT VISIBLE on this view';
  var out = [];
  for (var i = 0; i < s.rows.length; i++) out.push((i === s.rows.length - 1 ? 'SCRATCH: ' : ('r' + i + ': ')) + s.rows[i]);
  return out.join('\n');
})()
