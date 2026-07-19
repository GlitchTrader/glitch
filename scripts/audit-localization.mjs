import fs from "fs";
import path from "path";

const root = path.resolve(
  "ninjatrader/Glitch/AddOns/GlitchAddOn"
);
const langs = ["en-US", "pt-BR", "es-ES", "zh-CN", "fr-FR", "ru-RU"];
const tsv = fs.readFileSync(path.join(root, "Resources/Localization.tsv"), "utf8");
const catalog = new Map();

for (const line of tsv.split(/\r?\n/)) {
  if (!line || line.startsWith("#")) continue;
  const parts = line.split("\t");
  if (parts.length < 2) continue;
  const key = parts[0];
  if (!key || key.includes(" ")) continue;
  catalog.set(key, parts.slice(1));
}

const codeKeys = new Set();

function walk(dir) {
  for (const ent of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, ent.name);
    if (ent.isDirectory()) walk(p);
    else if (ent.name.endsWith(".cs")) {
      const s = fs.readFileSync(p, "utf8");
      for (const m of s.matchAll(/\bL\(\s*"([^"]+)"/g)) codeKeys.add(m[1]);
      for (const m of s.matchAll(/\bLf\(\s*"([^"]+)"/g)) codeKeys.add(m[1]);
      for (const m of s.matchAll(/Translate\(\s*"([^"]+)"/g)) codeKeys.add(m[1]);
      for (const m of s.matchAll(/TranslateFormat\(\s*\n?\s*"([^"]+)"/g)) codeKeys.add(m[1]);
      for (const m of s.matchAll(/BindLocalized\w*\([^,]+,\s*"([^"]+)"/g))
        codeKeys.add(m[1]);
      for (const m of s.matchAll(/GetLocalized\w*\(\s*"([^"]+)"/g))
        codeKeys.add(m[1]);
      for (const m of s.matchAll(/BuildSectionHeader\(\s*"([^"]+)"/g))
        codeKeys.add(m[1]);
      for (const m of s.matchAll(/BuildComplianceFeatureExpander\(\s*\n?\s*"([^"]+)"/g))
        codeKeys.add(m[1]);
    }
  }
}

walk(root);

const missingInTsv = [...codeKeys].filter((k) => !catalog.has(k)).sort();
const badRows = [];
const identicalToEn = [];

for (const [key, vals] of catalog) {
  if (vals.length < 6) badRows.push({ key, issue: `cols=${vals.length}` });
  else {
    langs.forEach((lang, i) => {
      if (!vals[i] || !vals[i].trim()) badRows.push({ key, issue: `empty ${lang}` });
    });
    const en = vals[0];
    for (let i = 1; i < 6; i++) {
      if (vals[i] === en && en.length > 3)
        identicalToEn.push({ key, lang: langs[i] });
    }
  }
}

const zhMissing = [];
const ruMissing = [];
for (const [key, vals] of catalog) {
  if (vals.length < 6) continue;
  if (!/[\u4e00-\u9fff]/.test(vals[3]) && vals[3].length > 2)
    zhMissing.push(key);
  if (!/[А-Яа-яЁё]/.test(vals[5]) && vals[5].length > 2)
    ruMissing.push(key);
}

console.log(JSON.stringify({
  catalogKeys: catalog.size,
  codeKeys: codeKeys.size,
  missingInTsv,
  badRows,
  identicalToEnCount: identicalToEn.length,
  identicalToEn: identicalToEn.slice(0, 50),
  zhNoCjk: zhMissing.slice(0, 30),
  ruNoCyrillic: ruMissing.slice(0, 30),
}, null, 2));
