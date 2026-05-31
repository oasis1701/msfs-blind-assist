'use strict';

// Decode FBW MCDU cell markup into accessible plain text.
// Tags: {green}{amber}{cyan}{white}{magenta}{yellow}{red}{inop} (color),
//       {small}{big} (size), {left}{right} (align), {sp} (nbsp), {end} (close).
// Mirrored 1:1 by MSFSBlindAssist/Services/FbwMcduFormat.cs — keep both in sync.

var COLOR_TAGS = ['green', 'amber', 'cyan', 'white', 'magenta', 'yellow', 'red', 'inop'];

var ANNUNCIATOR_ORDER = ['fail', 'fmgc', 'mcdu_menu', 'fm1', 'fm2', 'ind', 'rdy'];
var ANNUNCIATOR_LABELS = {
  fail: 'FAIL', fmgc: 'FMGC', mcdu_menu: 'MENU',
  fm1: 'FM1', fm2: 'FM2', ind: 'IND', rdy: 'RDY',
};

function parseSegments(cell) {
  var segments = [];
  var color = 'white';
  var text = '';
  var i = 0;
  while (i < cell.length) {
    if (cell[i] === '{') {
      var close = cell.indexOf('}', i);
      if (close !== -1) {
        var tag = cell.substring(i + 1, close);
        if (tag === 'sp') {
          text += ' ';
        } else if (COLOR_TAGS.indexOf(tag) !== -1) {
          if (text.length > 0) { segments.push({ color: color, text: text }); text = ''; }
          color = tag;
        } else if (tag === 'end') {
          if (text.length > 0) { segments.push({ color: color, text: text }); text = ''; }
          color = 'white';
        }
        // small/big/left/right/unknown tags: dropped
        i = close + 1;
        continue;
      }
    }
    text += cell[i];
    i++;
  }
  if (text.length > 0) { segments.push({ color: color, text: text }); }
  return segments;
}

function decodeCell(cell) {
  if (!cell) { return ''; }
  var segments = parseSegments(cell);
  var colors = {};
  for (var s = 0; s < segments.length; s++) {
    if (segments[s].text.trim().length > 0) { colors[segments[s].color] = true; }
  }
  var distinct = Object.keys(colors);
  var mixedGreen = distinct.length > 1 && colors['green'];
  var out = '';
  for (var k = 0; k < segments.length; k++) {
    var seg = segments[k];
    if (mixedGreen && seg.color === 'green' && seg.text.trim().length > 0) {
      out += '*' + seg.text;
    } else {
      out += seg.text;
    }
  }
  return out;
}

function litAnnunciators(ann) {
  var out = [];
  if (!ann) { return out; }
  for (var i = 0; i < ANNUNCIATOR_ORDER.length; i++) {
    var key = ANNUNCIATOR_ORDER[i];
    if (ann[key] && ANNUNCIATOR_LABELS[key]) { out.push(ANNUNCIATOR_LABELS[key]); }
  }
  return out;
}

function cell(row, idx) {
  return row && row[idx] != null ? row[idx] : '';
}

function decodeSide(side) {
  side = side || {};
  var lines = side.lines || [];
  var rows = [];
  for (var k = 0; k < 6; k++) {
    var label = lines[2 * k] || ['', '', ''];
    var value = lines[2 * k + 1] || ['', '', ''];
    rows.push({
      labelLeft: decodeCell(cell(label, 0)),
      labelRight: decodeCell(cell(label, 1)),
      labelCenter: decodeCell(cell(label, 2)),
      valueLeft: decodeCell(cell(value, 0)),
      valueRight: decodeCell(cell(value, 1)),
      valueCenter: decodeCell(cell(value, 2)),
    });
  }
  return {
    title: decodeCell(side.title || ''),
    page: decodeCell(side.page || ''),
    scratchpad: decodeCell(side.scratchpad || ''),
    arrows: side.arrows || [false, false, false, false],
    annunciators: litAnnunciators(side.annunciators),
    rows: rows,
  };
}

function joinColumns(left, center, right) {
  var parts = [];
  if (left && left.trim()) { parts.push(left.trim()); }
  if (center && center.trim()) { parts.push(center.trim()); }
  if (right && right.trim()) { parts.push(right.trim()); }
  return parts.join('   ');
}

function renderLines(decoded) {
  var out = [];
  if (decoded.annunciators.length) { out.push('Annunciators: ' + decoded.annunciators.join(', ')); }
  var titleLine = 'Title: ' + decoded.title;
  if (decoded.page) { titleLine += '   ' + decoded.page; }
  if (decoded.arrows[0]) { titleLine += ' ▲'; }
  if (decoded.arrows[1]) { titleLine += ' ▼'; }
  out.push(titleLine);
  for (var k = 0; k < 6; k++) {
    var r = decoded.rows[k];
    var labelText = joinColumns(r.labelLeft, r.labelCenter, r.labelRight);
    var valueText = joinColumns(r.valueLeft, r.valueCenter, r.valueRight);
    if (labelText.trim().length) { out.push('   ' + labelText); }
    out.push((k + 1) + ': ' + valueText);
  }
  out.push('Scratchpad: ' + decoded.scratchpad);
  return out;
}

module.exports = {
  parseSegments: parseSegments,
  decodeCell: decodeCell,
  litAnnunciators: litAnnunciators,
  decodeSide: decodeSide,
  joinColumns: joinColumns,
  renderLines: renderLines,
};
