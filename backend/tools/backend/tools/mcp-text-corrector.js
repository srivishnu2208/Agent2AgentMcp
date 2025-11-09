#!/usr/bin/env node
// Clean debug MCP corrector - guaranteed UTF-8 no BOM

const fs = require('fs');
const path = require('path');
const readline = require('readline');

const rl = readline.createInterface({ input: process.stdin, output: process.stdout });

console.error('[mcp-text-corrector] ready (clean debug)');

rl.on('line', (line) => {
  try {
    console.error('[mcp-in] ' + line);
    const msg = JSON.parse(line);
    if (msg && msg.type === 'invoke' && msg.tool === 'text_correct') {
      const id = msg.id || null;
      const rawText = (msg.args && msg.args.text) ? String(msg.args.text) : '';
      // very simple deterministic fix pipeline
      let t = rawText.replace(/\s+/g, ' ').trim();
      t = t.replace(/\bteh\b/gi, 'the');
      t = t.replace(/\bfrnace\b/gi, 'france');
      t = t.replace(/\bhyderbad\b/gi, 'hyderabad');
      t = t.replace(/\bhydrabad\b/gi, 'hyderabad');
      t = t.replace(/\btelengana\b/gi, 'telangana');
      // capitalize + punctuation
      if (t.length > 0) t = t.charAt(0).toUpperCase() + t.slice(1);
      if (!/[.?!]$/.test(t)) t = t + '?';
      console.error('[mcp-out-corrected] id=' + id + ' corrected=' + t);
      process.stdout.write(JSON.stringify({ id: id, type: 'result', result: { corrected: t } }) + '\n');
    } else if (msg && msg.type === 'ping') {
      process.stdout.write(JSON.stringify({ id: msg.id || null, type: 'pong' }) + '\n');
    } else {
      process.stdout.write(JSON.stringify({ id: msg.id || null, type: 'error', error: 'unknown_message' }) + '\n');
    }
  } catch (e) {
    console.error('[mcp-ex] ' + (e && e.stack ? e.stack : e));
    process.stdout.write(JSON.stringify({ id: null, type: 'error', error: 'invalid_json', details: String(e) }) + '\n');
  }
});
