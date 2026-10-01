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

var NO_BREAK_SPACE = '\u00a0';

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
    // FBW pads cells and draws its entry boxes ("[\xa0\xa0]") with U+00A0. It is a blank
    // like {sp}, so it becomes one here and nothing downstream has to know it exists.
    text += ch === NO_BREAK_SPACE ? ' ' : ch;
    i++;
  }
  if (text.length > 0) { segments.push({ color: color, text: text }); }
  return segments;
}

// Decoded cell text with its green-selection markers held beside it rather than in it:
// marks[j] is true when a '*' belongs in front of text[j]; marks is null when the cell has
// none. FBW pads a whole row to exactly 24 columns (INIT FUEL PRED sends ZFW/ZFWCG, the
// padding AND the BLOCK value in cell 0), so a '*' counted as a column pushed the
// right-hand value past column 24 and the clip deleted it ("*3.1*/*0137" with BLOCK fuel
// missing); positionMarked lays the columns out first and places the markers after.
function withStars(m) {
  if (!m.marks) { return m.text; }
  var out = '';
  for (var j = 0; j < m.text.length; j++) { out += (m.marks[j] ? '*' : '') + m.text[j]; }
  return out;
}

// Reconstruct an MCDU line positionally (24 cols): left-aligned left, right-aligned
// right, centred centre. Cells keep their own {sp} padding — FBW pads cells to
// column-align the display (e.g. F-PLN time "2053    " + speed ".78/ FL370");
// trimming the padding and re-centring used to run the time into the speed
// ("2053.78"). Spaces never overwrite, so overlapping padding can't erase a
// neighbouring cell's text. The text here is not decoded (the DCDU's), so a U+00A0 is
// made a plain space first, as parseSegments does for decoded cells.
// Trailing whitespace of the finished line is trimmed.
// A '*' in the text is an ordinary column (the DCDU's key stars are real content).
function positionLine(left, center, right, width) {
  return positionMarked(undecoded(left), undecoded(center), undecoded(right), width);
}

function undecoded(text) {
  return { text: (text || '').split(NO_BREAK_SPACE).join(' '), marks: null };
}

// positionLine for decoded cells. The columns are laid out without the green-selection
// markers, then each marker is written in: into the blank column in front of its text when
// the gap there has one to spare (a gap after text keeps one blank, so "ALT *FL370" keeps
// its space), otherwise inserted, and an inserted '*' is paid back by the next gap with a
// blank to spare. So everything after a gap stays in its FBW column — the right-hand
// values line up down the page, which a braille display depends on — and the line is
// wider than `width` only when no gap can pay. The blanks inside an entry box ("[  ]",
// which FBW draws from the same U+00A0 it pads with) are the width of the field, never a
// gap to pay from.
function positionMarked(l, c, r, width) {
  width = width || 24;
  var buf = new Array(width);
  var marked = null;
  for (var i = 0; i < width; i++) { buf[i] = ' '; }
  function place(m, start) {
    for (var j = 0; j < m.text.length; j++) {
      var p = start + j;
      if (m.text[j] !== ' ' && p >= 0 && p < width) {
        buf[p] = m.text[j];
        var mark = !!(m.marks && m.marks[j]);
        if (mark && !marked) { marked = new Array(width).fill(false); }
        if (marked) { marked[p] = mark; }
      }
    }
  }
  place(l, 0);
  if (c.text.replace(/\s/g, '').length) { place(c, Math.max(0, Math.floor((width - c.text.length) / 2))); }
  if (r.text.replace(/\s/g, '').length) { place(r, Math.max(0, width - r.text.length)); }
  if (!marked) { return buf.join('').replace(/\s+$/, ''); }
  var out = '';
  var owed = 0; // inserted stars not yet paid back by a gap
  var k = 0;
  while (k < width) {
    if (buf[k] !== ' ') {
      // A value that follows a gap had its '*' written by the gap.
      if (marked[k] && (k === 0 || buf[k - 1] !== ' ')) { out += '*'; owed++; }
      out += buf[k];
      k++;
      continue;
    }
    var end = k;
    while (end < width && buf[end] === ' ') { end++; }
    if (end === width) { break; }
    // Blanks the gap can give up: all of a leading gap, all but one after text, and none
    // inside an entry box ("[  ]"), whose blanks are the width of the field.
    var box = k > 0 && buf[k - 1] === '[' && buf[end] === ']';
    var spare = box ? 0 : end - k - (k === 0 ? 0 : 1);
    var taken = marked[end] && spare > 0 ? 1 : 0; // the '*' takes a blank
    if (marked[end] && taken === 0) { owed++; } // or is inserted
    var repay = Math.min(owed, spare - taken);
    owed -= repay;
    out += ' '.repeat(end - k - taken - repay);
    if (marked[end]) { out += '*'; }
    k = end;
  }
  return out.replace(/\s+$/, '');
}

function decodeCell(cell) {
  return withStars(decodeCellMarked(cell));
}

// decodeCell with the green-selection markers kept beside the text, for cells that still
// have to go through positionMarked.
function decodeCellMarked(cell) {
  if (!cell) { return { text: '', marks: null }; }
  var segments = parseSegments(cell);
  var colors = {};
  for (var s = 0; s < segments.length; s++) {
    if (segments[s].text.trim().length > 0) { colors[segments[s].color] = true; }
  }
  var colorCount = Object.keys(colors).length;
  var mixedGreen = colorCount > 1 && colors['green'];
  var out = '';
  var markAt = null;
  // FBW sends one green value as touching pieces ("10.2", "/", "0213"). A pilot sees one
  // green value, so a piece that continues a green run gets no '*' of its own.
  var inGreenRun = false;
  for (var k = 0; k < segments.length; k++) {
    var seg = segments[k];
    var green = mixedGreen && seg.color === 'green' && seg.text.trim().length > 0;
    if (green) {
      var trimmed = seg.text.replace(/^\s+/, '');
      var leading = seg.text.slice(0, seg.text.length - trimmed.length);
      out += leading;
      if (!(inGreenRun && leading.length === 0)) { (markAt = markAt || []).push(out.length); }
      out += trimmed;
    } else {
      out += seg.text;
    }
    inGreenRun = !!green && !/\s$/.test(seg.text);
  }
  if (!markAt) { return { text: out, marks: null }; }
  var marks = new Array(out.length).fill(false);
  markAt.forEach(function (j) { marks[j] = true; });
  return { text: out, marks: marks };
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
    // Kept marked so positionMarked lays out the true 24 columns.
    var m = {
      labelLeft: decodeCellMarked(cell(label, 0)),
      labelRight: decodeCellMarked(cell(label, 1)),
      labelCenter: decodeCellMarked(cell(label, 2)),
      valueLeft: decodeCellMarked(cell(value, 0)),
      valueRight: decodeCellMarked(cell(value, 1)),
      valueCenter: decodeCellMarked(cell(value, 2)),
    };
    rows.push({
      labelLeft: withStars(m.labelLeft),
      labelRight: withStars(m.labelRight),
      labelCenter: withStars(m.labelCenter),
      valueLeft: withStars(m.valueLeft),
      valueRight: withStars(m.valueRight),
      valueCenter: withStars(m.valueCenter),
      labelText: positionMarked(m.labelLeft, m.labelCenter, m.labelRight),
      valueText: positionMarked(m.valueLeft, m.valueCenter, m.valueRight),
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
