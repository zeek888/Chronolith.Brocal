// Verifies the translations of Brocal: every key used in the code exists in es.json and en.json, and both
// files have the same keys. Dynamic keys ('nav.' + item.key, enums…) are listed in the JSON files directly.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, extname } from 'node:path';

const root = new URL('../src/app/', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');
const files = [];
(function walk(dir) {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) {
      walk(path);
    } else if (['.ts', '.html'].includes(extname(path))) {
      files.push(path);
    }
  }
})(root);

const patterns = [
  /'([a-z][a-zA-Z]*(?:\.[a-zA-Z0-9]+)+)'\s*\|\s*t\b/g,
  /\(\s*'([a-z][a-zA-Z]*(?:\.[a-zA-Z0-9]+)+)'\s*\?\?/g,
  // i18n.t('…') and the `t` handed to computed table cells: t('…').
  /(?:\.|\b)t\(\s*'([a-z][a-zA-Z]*(?:\.[a-zA-Z0-9]+)+)'/g,
  /\b(?:label|hint|addLabel|title|titleKey):\s*'([a-z][a-zA-Z]*(?:\.[a-zA-Z0-9]+)+)'/g,
  /\?\s*'([a-z][a-zA-Z]*(?:\.[a-zA-Z0-9]+)+)'\s*:\s*'([a-z][a-zA-Z]*(?:\.[a-zA-Z0-9]+)+)'/g,
];

const used = new Set();
for (const file of files) {
  const text = readFileSync(file, 'utf8');
  for (const pattern of patterns) {
    for (const match of text.matchAll(pattern)) {
      for (const key of match.slice(1)) {
        if (key) {
          used.add(key.startsWith('brocal.') ? key.slice('brocal.'.length) : key);
        }
      }
    }
  }
}

function flatten(object, prefix = '', out = new Map()) {
  for (const [key, value] of Object.entries(object)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (value && typeof value === 'object') {
      flatten(value, path, out);
    } else {
      out.set(path, value);
    }
  }

  return out;
}

const es = flatten(JSON.parse(readFileSync(join(root, 'i18n', 'es.json'), 'utf8')));
const en = flatten(JSON.parse(readFileSync(join(root, 'i18n', 'en.json'), 'utf8')));

// Literal file names, URLs and ids that the patterns may catch.
const ignored = (key) => /\.(json|png|svg|pdf|xlsx|xml|zip|jpg)$/.test(key) || key.startsWith('brocal.');
const missingEs = [...used].filter((k) => !ignored(k) && !es.has(k)).sort();
const missingEn = [...es.keys()].filter((k) => !en.has(k)).sort();
const extraEn = [...en.keys()].filter((k) => !es.has(k)).sort();

let failed = false;
if (missingEs.length) {
  failed = true;
  console.error(`Llaves usadas sin texto en es.json (${missingEs.length}):\n  ${missingEs.join('\n  ')}`);
}

if (missingEn.length) {
  failed = true;
  console.error(`Llaves de es.json sin traducción en en.json (${missingEn.length}):\n  ${missingEn.join('\n  ')}`);
}

if (extraEn.length) {
  failed = true;
  console.error(`Llaves de en.json que no existen en es.json (${extraEn.length}):\n  ${extraEn.join('\n  ')}`);
}

if (failed) {
  process.exit(1);
}

console.log(`Traducciones completas: ${es.size} textos, ${used.size} llaves usadas en el código.`);
