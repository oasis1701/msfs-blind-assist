'use strict';
var WebSocket = require('ws');
var fs = require('fs');
var fmt = require('./mcdu-format');

function parseArgs(argv) {
  var args = { _: [], side: 'left', host: 'localhost:8380', export: null };
  for (var i = 0; i < argv.length; i++) {
    var a = argv[i];
    if (a === '--side') { args.side = argv[++i]; }
    else if (a === '--host') { args.host = argv[++i]; }
    else if (a === '--export') { args.export = argv[++i]; }
    else { args._.push(a); }
  }
  return args;
}

function wsUrl(host) { return 'ws://' + host + '/interfaces/v1/mcdu'; }

function connect(host, onOpen, onUpdate) {
  var ws = new WebSocket(wsUrl(host));
  ws.on('open', function () { onOpen(ws); });
  ws.on('message', function (data) {
    var msg = data.toString();
    var idx = msg.indexOf(':');
    var type = idx === -1 ? msg : msg.substring(0, idx);
    if (type === 'update') {
      try { onUpdate(JSON.parse(msg.substring(idx + 1))); } catch (e) { /* ignore parse errors */ }
    }
  });
  ws.on('error', function (e) { console.error('WS error:', e.message); });
  ws.on('close', function () { /* watch relies on reconnect by re-run; press/type exit on their own */ });
  return ws;
}

function printDecoded(content, side) {
  var sideObj = content[side] || {};
  var decoded = fmt.decodeSide(sideObj);
  console.log('\n===== ' + side.toUpperCase() + ' MCDU @ ' + new Date().toISOString() + ' =====');
  fmt.renderLines(decoded).forEach(function (l) { console.log(l); });
}

function cmdWatch(args) {
  var stream = args.export ? fs.createWriteStream(args.export, { flags: 'a' }) : null;
  connect(args.host, function (ws) {
    console.error('Connected ' + wsUrl(args.host) + ' (side=' + args.side + '). Ctrl+C to exit.');
    ws.send('requestUpdate');
  }, function (content) {
    printDecoded(content, args.side);
    if (stream) {
      var sideObj = content[args.side] || {};
      stream.write(JSON.stringify({
        ts: Date.now(), side: args.side, raw: sideObj, decoded: fmt.decodeSide(sideObj),
      }) + '\n');
    }
  });
}

function cmdPress(args) {
  var key = args._[1];
  if (!key) { console.error('usage: probe.js press <KEY> [--side left|right]'); process.exit(1); }
  var got = false;
  connect(args.host, function (ws) {
    ws.send('event:' + args.side + ':' + key.toUpperCase());
    setTimeout(function () { ws.send('requestUpdate'); }, 150);
  }, function (content) {
    if (got) { return; }
    got = true;
    printDecoded(content, args.side);
    setTimeout(function () { process.exit(0); }, 100);
  });
  setTimeout(function () { if (!got) { console.error('No update received.'); process.exit(2); } }, 4000);
}

function charToKey(c) {
  if (/[A-Z0-9]/.test(c)) { return c; }
  switch (c) {
    case '.': return 'DOT';
    case '/': return 'DIV';
    case '-': case '+': return 'PLUSMINUS';
    case ' ': return 'SP';
    case '*': return 'OVFY';
    default: return null;
  }
}

function cmdType(args) {
  var text = (args._[1] || '').toUpperCase();
  var keys = [];
  for (var i = 0; i < text.length; i++) { var k = charToKey(text[i]); if (k) { keys.push(k); } }
  connect(args.host, function (ws) {
    var j = 0;
    (function next() {
      if (j >= keys.length) { setTimeout(function () { ws.send('requestUpdate'); }, 100); return; }
      ws.send('event:' + args.side + ':' + keys[j]); j++;
      setTimeout(next, 60);
    })();
  }, function (content) {
    printDecoded(content, args.side);
    setTimeout(function () { process.exit(0); }, 250);
  });
  setTimeout(function () { process.exit(0); }, 10000);
}

function cmdReplay(args) {
  var file = args._[1];
  if (!file) { console.error('usage: probe.js replay <file.jsonl>'); process.exit(1); }
  var lines = fs.readFileSync(file, 'utf8').split('\n').filter(Boolean);
  lines.forEach(function (line) {
    try {
      var rec = JSON.parse(line);
      var decoded = rec.decoded || fmt.decodeSide(rec.raw || {});
      console.log('\n===== ' + (rec.side || '?').toUpperCase() + ' @ ' + new Date(rec.ts).toISOString() + ' =====');
      fmt.renderLines(decoded).forEach(function (l) { console.log(l); });
    } catch (e) { /* skip malformed line */ }
  });
}

var args = parseArgs(process.argv.slice(2));
var cmd = args._[0];
if (cmd === 'watch') { cmdWatch(args); }
else if (cmd === 'press') { cmdPress(args); }
else if (cmd === 'type') { cmdType(args); }
else if (cmd === 'replay') { cmdReplay(args); }
else {
  console.error('Commands:\n' +
    '  watch [--side left|right] [--host h:p] [--export file.jsonl]\n' +
    '  press <KEY> [--side ...] [--host ...]\n' +
    '  type "<text>" [--side ...] [--host ...]\n' +
    '  replay <file.jsonl>');
  process.exit(1);
}
