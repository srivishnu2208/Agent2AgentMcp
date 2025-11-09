#!/usr/bin/env node
// Debug MCP text corrector - logs input & output for troubleshooting

const fs = require('fs');
const path = require('path');
const readline = require('readline');

const rl = readline.createInterface({ input: process.stdin, output: process.stdout });

const ENT_FILE = path.join(__dirname, 'entities.json');
let entities = {};
try {
  if (fs.existsSync(ENT_FILE)) {
    const raw = fs.readFileSync(ENT_FILE, 'utf8');
    const arr = JSON.parse(raw);
    for (const e of arr) {
      if (e.id && Array.isArray(e.aliases)) {
        for (const a of e.aliases) entities[a.toLowerCase()] = e.id;
      }
    }
  }
} catch (e) {
  console.error('[mcp] failed loading entities.json:', e.message);
}

const FIXES = {
  "teh": "the",
  "frnace": "france",
  "frnce": "france",
  "hyderbad": "hyderabad",
  "hydrabad": "hyderabad",
  "telengana": "telangana",
  "pincodeof": "pincode of",
  "pincode": "PIN code",
  "pin code": "PIN code"
};

function normalizeSpaces(s) { return s.replace(/\s+/g, ' ').trim(); }

function applySimpleFixes(s) {
  let out = s;
  for (const [k, v] of Object.entries(FIXES)) {
    const re = new RegExp('\\b' + k.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '\\b', 'gi');
    out = out.replace(re, (m) => {
      if (m[0] && m[0] === m[0].toUpperCase()) return v.charAt(0).toUpperCase() + v.slice(1);
      return v;
    });
  }
  return out;
}

function normalizeEntities(s) {
  if (!s) return s;
  let out = s;
  for (const [alias, canon] of Object.entries(entities)) {
    const re = new RegExp('\\b' + alias.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '\\b', 'gi');
    out = out.replace(re, (m) => {
      if (canon && canon.length > 0) {
        if (m[0] && m[0] === m[0].toUpperCase()) return canon.charAt(0).toUpperCase() + canon.slice(1);
        return canon;
      }
      return m;
    });
  }
  return out;
}

function finalizeSentence(s) {
  if (!s) return s;
  let out = s.trim();
  out = out.charAt(0).toUpperCase() + out.slice(1);
  if (!/[.?!]$/.test(out)) out += '?';
  return out;
}

console.error('[mcp-text-corrector] ready (debug)');

rl.on('line', (line) => {
  try {
    // log raw incoming JSON for debugging
    console.error('[mcp-in] ' + line);

    const msg = JSON.parse(line);
    if (msg && msg.type === 'invoke' && msg.tool === 'text_correct') {
      const id = msg.id || null;
      const rawText = (msg.args && msg.args.text) ? String(msg.args.text) : '';

      // pipeline
      let t = normalizeSpaces(rawText);
      t = applySimpleFixes(t);
      t = normalizeEntities(t);
      t = finalizeSentence(t);

      // log the corrected value
      console.error('[mcp-out-corrected] id=' + id + ' corrected=' + t);

      const out = { id: id, type: 'result', result: { corrected: t } };
      process.stdout.write(JSON.stringify(out) + '\n');
    } else if (msg && msg.type === 'ping') {
      console.error('[mcp-ping] id=' + (msg.id || null));
      process.stdout.write(JSON.stringify({ id: msg.id || null, type: 'pong' }) + '\n');
    } else {
      process.stdout.write(JSON.stringify({ id: msg.id || null, type: 'error', error: 'unknown_message' }) + '\n');
    }
  } catch (e) {
    console.error('[mcp-ex] ' + e.stack || e.message);
    process.stdout.write(JSON.stringify({ id: null, type: 'error', error: 'invalid_json', details: String(e) }) + '\n');
  }
});
