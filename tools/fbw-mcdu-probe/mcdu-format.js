'use strict';

// Decode FBW MCDU cell markup into accessible plain text.
// Tags: {green}{amber}{cyan}{white}{magenta}{yellow}{red}{inop} (color),
//       {small}{big} (size), {left}{right} (align), {sp} (nbsp), {end} (close).
// Mirrored 1:1 by MSFSBlindAssist/Services/FbwMcduFormat.cs — keep both in sync.

var COLOR_TAGS = ['green', 'amber', 'cyan', 'white', 'magenta', 'yellow', 'red', 'inop'];
var DROP_TAGS = ['small', 'big', 'left', 'right']; // size/align — styling only

function isKnownTag(tag) {
  return tag === 'sp' || tag === 'end' || COLOR_TAGS.indexOf(tag) !== -1 || DROP_TAGS.indexOf(tag) !== -1;
}

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
    var ch = cell[i];
    if (ch === '{') {
      var close = cell.indexOf('}', i);
      if (close !== -1) {
        var tag = cell.substring(i + 1, close);
        if (isKnownTag(tag)) {
          if (tag === 'sp') {
            text += ' ';
          } else if (COLOR_TAGS.indexOf(tag) !== -1) {
            if (text.length > 0) { segments.push({ color: color, text: text }); text = ''; }
            color = tag;
          } else if (tag === 'end') {
            if (text.length > 0) { segments.push({ color: color, text: text }); text = ''; }
            color = 'white';
          }
          // small/big/left/right: styling only, dropped
          i = close + 1;
          continue;
        }
      }
      // A lone '{' that does NOT open a known {tag} is the FBW MCDU's LSK arrow /
      // bracket glyph (e.g. "{08L" = the selectable runway prompt). Drop the glyph and
      // keep the content ("08L"); the old greedy parse ate everything up to the next
      // '}', deleting the runway designator and breaking the DEP/ARR pages.
      i++;
      continue;
    }
    if (ch === '}') {
      // Stray right-side arrow/bracket glyph (real {tag} closers are consumed above). Drop.
      i++;
      continue;
    }
    text += ch;
    i++;
  }
  if (text.length > 0) { segments.push({ color: color, text: text }); }
  return segments;
}

// Stand-in for the '*' green-selection marker while a line is being laid out. It takes
// no column: positionLine attaches it to the character that follows and writes it out
// as '*' only after the columns are fixed. FBW pads a whole row to exactly 24 columns
// (INIT FUEL PRED sends ZFW/ZFWCG, the padding AND the BLOCK value in cell 0), so a real
// '*' counted as a column pushed the right-hand value past column 24 and the clip
// deleted it ("*3.1*/*0137" with BLOCK fuel missing).
var MARKER = '\u0001';

// Reconstruct an MCDU line positionally (24 cols): left-aligned left, right-aligned
// right, centred centre. Cells keep their own {sp} padding — FBW pads cells to
// column-align the display (e.g. F-PLN time "2053    " + speed ".78/ FL370");
// trimming the padding and re-centring used to run the time into the speed
// ("2053.78"). Spaces never overwrite, so overlapping padding can't erase a
// neighbouring cell's text. Trailing whitespace of the finished line is trimmed.
// Green-selection markers take no column (see MARKER), so the finished line can be
// longer than `width` by one '*' per marker.
function positionLine(left, center, right, width) {
  width = width || 24;
  var buf = new Array(width);
  var marked = new Array(width);
  for (var i = 0; i < width; i++) { buf[i] = ' '; marked[i] = false; }
  function strip(s) {
    var text = '';
    var marks = [];
    var pending = false;
    for (var j = 0; j < s.length; j++) {
      if (s[j] === MARKER) { pending = true; continue; }
      text += s[j];
      marks.push(pending);
      pending = false;
    }
    return { text: text, marks: marks };
  }
  function place(cellText, start) {
    for (var j = 0; j < cellText.text.length; j++) {
      var p = start + j;
      if (cellText.text[j] !== ' ' && p >= 0 && p < width) {
        buf[p] = cellText.text[j];
        marked[p] = cellText.marks[j];
      }
    }
  }
  var l = strip(left || '');
  var c = strip(center || '');
  var r = strip(right || '');
  place(l, 0);
  if (c.text.replace(/\s/g, '').length) { place(c, Math.max(0, Math.floor((width - c.text.length) / 2))); }
  if (r.text.replace(/\s/g, '').length) { place(r, Math.max(0, width - r.text.length)); }
  var out = '';
  for (var k = 0; k < width; k++) { out += (marked[k] ? '*' : '') + buf[k]; }
  return out.replace(/\s+$/, '');
}

function unmark(s) {
  return s.split(MARKER).join('*');
}

function decodeCell(cell) {
  return unmark(decodeCellMarked(cell));
}

// decodeCell with each green-selection marker left as the zero-width MARKER, for text
// that still has to go through positionLine.
function decodeCellMarked(cell) {
  if (!cell) { return ''; }
  var segments = parseSegments(cell);
  var colors = {};
  for (var s = 0; s < segments.length; s++) {
    if (segments[s].text.trim().length > 0) { colors[segments[s].color] = true; }
  }
  var colorCount = Object.keys(colors).length;
  var mixedGreen = colorCount > 1 && colors['green'];
  var out = '';
  for (var k = 0; k < segments.length; k++) {
    var seg = segments[k];
    if (mixedGreen && seg.color === 'green' && seg.text.trim().length > 0) {
      var trimmed = seg.text.replace(/^\s+/, '');
      var leading = seg.text.slice(0, seg.text.length - trimmed.length);
      out += leading + MARKER + trimmed;
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
    // Kept marked (zero-width markers) so positionLine lays out the true 24 columns.
    var m = {
      labelLeft: decodeCellMarked(cell(label, 0)),
      labelRight: decodeCellMarked(cell(label, 1)),
      labelCenter: decodeCellMarked(cell(label, 2)),
      valueLeft: decodeCellMarked(cell(value, 0)),
      valueRight: decodeCellMarked(cell(value, 1)),
      valueCenter: decodeCellMarked(cell(value, 2)),
    };
    rows.push({
      labelLeft: unmark(m.labelLeft),
      labelRight: unmark(m.labelRight),
      labelCenter: unmark(m.labelCenter),
      valueLeft: unmark(m.valueLeft),
      valueRight: unmark(m.valueRight),
      valueCenter: unmark(m.valueCenter),
      labelText: positionLine(m.labelLeft, m.labelCenter, m.labelRight),
      valueText: positionLine(m.valueLeft, m.valueCenter, m.valueRight),
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
  if (decoded.arrows[2]) { titleLine += ' ◄'; }
  if (decoded.arrows[3]) { titleLine += ' ►'; }
  out.push(titleLine);
  for (var k = 0; k < 6; k++) {
    var r = decoded.rows[k] || { labelLeft: '', labelRight: '', labelCenter: '', valueLeft: '', valueRight: '', valueCenter: '' };
    // decodeSide positions the lines from the marked cells; a recording made before it did
    // falls back to positioning the '*'-bearing cells (the old, clipping behaviour).
    var labelText = r.labelText != null ? r.labelText : positionLine(r.labelLeft, r.labelCenter, r.labelRight);
    var valueText = r.valueText != null ? r.valueText : positionLine(r.valueLeft, r.valueCenter, r.valueRight);
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
  positionLine: positionLine,
  renderLines: renderLines,
};
