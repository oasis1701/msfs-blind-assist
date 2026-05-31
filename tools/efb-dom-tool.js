#!/usr/bin/env node
// efb-dom-tool.js — live CDP scraper/clicker for the FBW A32NX EFB (Coherent GT page 16).
// Requires Node.js 18+. Run from the repo root.
//
// Usage:
//   node tools/efb-dom-tool.js state              check bridge JS loaded/connected state
//   node tools/efb-dom-tool.js dom                dump page body innerText (first 3000 chars)
//   node tools/efb-dom-tool.js find <selector>    querySelector count + first element text
//   node tools/efb-dom-tool.js click <text>       click first button-like element matching text
//   node tools/efb-dom-tool.js eval <expr>        eval arbitrary JS, print result
//   node tools/efb-dom-tool.js inject             re-inject bridge JS from Resources/
//   node tools/efb-dom-tool.js pages              list all Coherent GT devtools pages

'use strict';
const net    = require('net');
const crypto = require('crypto');
const http   = require('http');
const fs     = require('fs');
const path   = require('path');

const CDP_HOST = '127.0.0.1';
const CDP_PORT = 19999;
const EFB_PAGE_TITLE_SUFFIX = '- EFB';
const BRIDGE_JS_PATH = path.join(__dirname, '..', 'MSFSBlindAssist', 'Resources', 'coherent-a32nx-flypad-agent.js');

// ── WebSocket helpers ─────────────────────────────────────────────────────────

function buildWsFrame(text) {
    const payload = Buffer.from(text, 'utf8');
    const len = payload.length;
    const mask = crypto.randomBytes(4);
    let header;
    if (len <= 125) {
        header = Buffer.alloc(6);
        header[0] = 0x81; header[1] = 0x80 | len;
        mask.copy(header, 2);
    } else if (len <= 65535) {
        header = Buffer.alloc(8);
        header[0] = 0x81; header[1] = 0xFE;
        header[2] = (len >> 8) & 0xFF; header[3] = len & 0xFF;
        mask.copy(header, 4);
    } else {
        header = Buffer.alloc(14);
        header[0] = 0x81; header[1] = 0xFF;
        const hi = Math.floor(len / 0x100000000), lo = len >>> 0;
        header.writeUInt32BE(hi, 2); header.writeUInt32BE(lo, 6);
        mask.copy(header, 10);
    }
    const masked = Buffer.alloc(len);
    for (let i = 0; i < len; i++) masked[i] = payload[i] ^ mask[i % 4];
    return Buffer.concat([header, masked]);
}

function cdpEval(pageId, expression, awaitPromise, timeoutMs) {
    awaitPromise = awaitPromise || false;
    timeoutMs = timeoutMs || 8000;
    return new Promise((resolve, reject) => {
        const wsKey = crypto.randomBytes(16).toString('base64');
        const sock = new net.Socket();
        let buf = Buffer.alloc(0);
        let headerDone = false;
        let responded = false;
        const timeout = setTimeout(() => {
            if (!responded) { responded = true; sock.destroy(); reject(new Error('CDP timeout')); }
        }, timeoutMs);

        sock.connect(CDP_PORT, CDP_HOST, () => {
            sock.write([
                `GET /devtools/page/${pageId} HTTP/1.1`,
                `Host: ${CDP_HOST}:${CDP_PORT}`,
                'Upgrade: websocket',
                'Connection: Upgrade',
                `Sec-WebSocket-Key: ${wsKey}`,
                'Sec-WebSocket-Version: 13',
                '', ''
            ].join('\r\n'));
        });

        sock.on('data', chunk => {
            buf = Buffer.concat([buf, chunk]);
            if (!headerDone) {
                const str = buf.toString('ascii');
                if (!str.includes('\r\n\r\n')) return;
                if (!str.includes('101')) {
                    sock.destroy();
                    reject(new Error('WebSocket upgrade refused: ' + str.split('\r\n')[0]));
                    return;
                }
                headerDone = true;
                const sepIdx = buf.indexOf('\r\n\r\n');
                buf = buf.slice(sepIdx + 4);
                const msg = JSON.stringify({ id: 1, method: 'Runtime.evaluate',
                    params: { expression, returnByValue: true, awaitPromise } });
                sock.write(buildWsFrame(msg));
                return;
            }

            // Parse incoming WebSocket frame (server→client, unmasked)
            if (buf.length < 2) return;
            const lb = buf[1] & 0x7F;
            let ps = lb <= 125 ? 2 : (lb === 126 ? 4 : 10);
            let pl = lb <= 125 ? lb : (lb === 126 ? buf.readUInt16BE(2) : -1);
            if (pl < 0 || buf.length < ps + pl) return;

            const text = buf.slice(ps, ps + pl).toString('utf8');
            if (!responded) {
                responded = true;
                clearTimeout(timeout);
                sock.destroy();
                try { resolve(JSON.parse(text)); }
                catch (e) { resolve({ raw: text.slice(0, 500) }); }
            }
        });

        sock.on('error', e => {
            if (!responded) { responded = true; clearTimeout(timeout); reject(e); }
        });
    });
}

// ── Page discovery ────────────────────────────────────────────────────────────

function getPages() {
    return new Promise((resolve, reject) => {
        http.get(`http://${CDP_HOST}:${CDP_PORT}/pagelist.json`, res => {
            let data = '';
            res.on('data', d => data += d);
            res.on('end', () => {
                try { resolve(JSON.parse(data)); }
                catch (e) { reject(new Error('Bad pagelist JSON: ' + e.message)); }
            });
        }).on('error', e => reject(new Error('Cannot reach Coherent GT devtools at port 19999: ' + e.message)));
    });
}

async function findEfbPageId() {
    const pages = await getPages();
    for (const p of pages) {
        const title = p.title || '';
        if (title.endsWith(EFB_PAGE_TITLE_SUFFIX)) return String(p.id);
    }
    throw new Error('EFB page not found in pagelist. Is the FBW A32NX loaded?');
}

// ── Result extractor ──────────────────────────────────────────────────────────

function extractValue(cdpResult) {
    if (!cdpResult || !cdpResult.result) return cdpResult;
    const r = cdpResult.result;
    if (r.exceptionDetails) {
        const ex = r.exceptionDetails;
        return { error: (ex.exception && ex.exception.description) || ex.text || 'exception' };
    }
    if (!r.result) return r;
    const res = r.result;
    if (res.value !== undefined) return res.value;
    if (res.type === 'undefined') return undefined;
    return res;
}

// ── Commands ──────────────────────────────────────────────────────────────────

async function cmdPages() {
    const pages = await getPages();
    console.log(`${pages.length} Coherent GT pages:\n`);
    for (const p of pages) {
        console.log(`  id=${p.id}  title="${p.title}"  url=${(p.url || '').substring(0, 80)}`);
    }
}

async function cmdState(pageId) {
    const expr = 'JSON.stringify({ installed: !!window.__MSFSBA_FLYPAD, ' +
        'ping: window.__MSFSBA_FLYPAD ? window.__MSFSBA_FLYPAD.ping() : null, ' +
        'page: window.__MSFSBA_FLYPAD ? JSON.parse(window.__MSFSBA_FLYPAD.scrape()).page : null })';
    const result = await cdpEval(pageId, expr);
    const raw = extractValue(result);
    console.log('Bridge state on page ' + pageId + ':');
    console.log(typeof raw === 'string' ? JSON.parse(raw) : raw);
}

async function cmdDom(pageId) {
    const result = await cdpEval(pageId,
        'document.body ? document.body.innerText.substring(0, 3000) : "(no body)"');
    console.log('Page ' + pageId + ' body text (first 3000 chars):\n');
    console.log(extractValue(result) || '(empty)');
}

async function cmdFind(pageId, selector) {
    const expr = `(function() {
        var els = document.querySelectorAll(${JSON.stringify(selector)});
        var first = els[0] ? els[0].textContent.trim().substring(0, 120) : null;
        return JSON.stringify({ count: els.length, first: first });
    })()`;
    const result = await cdpEval(pageId, expr);
    const raw = extractValue(result);
    console.log('find(' + selector + ') on page ' + pageId + ':');
    console.log(typeof raw === 'string' ? JSON.parse(raw) : raw);
}

async function cmdClick(pageId, text) {
    const expr = `(function() {
        var lower = ${JSON.stringify(text.toLowerCase())};
        var candidates = document.querySelectorAll(
            'button, [role="button"], div[tabindex], span[tabindex], div[onclick]');
        for (var i = 0; i < candidates.length; i++) {
            if (candidates[i].textContent.trim().toLowerCase().indexOf(lower) !== -1) {
                candidates[i].click();
                return JSON.stringify({ clicked: candidates[i].textContent.trim().substring(0, 80) });
            }
        }
        return JSON.stringify({ error: 'No element found matching: ' + lower });
    })()`;
    const result = await cdpEval(pageId, expr);
    const raw = extractValue(result);
    console.log(typeof raw === 'string' ? JSON.parse(raw) : raw);
}

async function cmdEval(pageId, expression) {
    const result = await cdpEval(pageId, expression);
    const val = extractValue(result);
    console.log('eval result:');
    console.log(val);
}

async function cmdInject(pageId) {
    if (!fs.existsSync(BRIDGE_JS_PATH)) {
        console.error('Bridge JS not found at:', BRIDGE_JS_PATH);
        process.exit(1);
    }
    const bridgeJs = fs.readFileSync(BRIDGE_JS_PATH, 'utf8');
    console.log('Injecting bridge JS (' + bridgeJs.length + ' bytes) into page ' + pageId + '...');
    const result = await cdpEval(pageId, bridgeJs, false, 10000);
    console.log('Inject result:', JSON.stringify(result).substring(0, 200));

    // Wait 2 s then report state
    await new Promise(res => setTimeout(res, 2000));
    await cmdState(pageId);
}

// ── Entry point ───────────────────────────────────────────────────────────────

async function main() {
    const [,, cmd, ...args] = process.argv;
    if (!cmd || cmd === 'help' || cmd === '--help') {
        console.log([
            'Usage: node tools/efb-dom-tool.js <command> [args]',
            '',
            '  pages                   List all Coherent GT devtools pages',
            '  state                   Check bridge JS load/connect state on EFB page',
            '  dom                     Dump EFB page body text (first 3000 chars)',
            '  find <selector>         Count elements matching CSS selector + show first text',
            '  click <text>            Click first button-like element whose text contains <text>',
            '  eval <expression>       Eval JS expression and print result',
            '  inject                  Re-inject bridge JS from Resources/ into EFB page',
            '  scrape                  Scrape the flyPad via the installed agent',
            '',
            'The EFB page is auto-detected by title suffix "- EFB" in the pagelist.',
            'Run from the repository root. MSFS must be running with FBW A32NX loaded.',
        ].join('\n'));
        return;
    }

    if (cmd === 'pages') { await cmdPages(); return; }

    const pageId = await findEfbPageId();

    switch (cmd) {
        case 'state':   await cmdState(pageId); break;
        case 'dom':     await cmdDom(pageId); break;
        case 'find':    await cmdFind(pageId, args.join(' ') || 'button'); break;
        case 'click':   await cmdClick(pageId, args.join(' ')); break;
        case 'eval':    await cmdEval(pageId, args.join(' ')); break;
        case 'inject':  await cmdInject(pageId); break;
        case 'scrape':  await cmdEval(pageId, 'window.__MSFSBA_FLYPAD ? window.__MSFSBA_FLYPAD.scrape() : "NO_AGENT"'); break;
        default:
            console.error('Unknown command:', cmd);
            console.error('Run "node tools/efb-dom-tool.js help" for usage.');
            process.exit(1);
    }
}

main().catch(e => {
    console.error('Error:', e.message);
    process.exit(1);
});
