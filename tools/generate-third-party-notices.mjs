// 从已锁定并安装的 npm 包提取许可原文；不访问服务凭据或外部账户。
import { readFileSync, readdirSync, mkdirSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const lock = JSON.parse(readFileSync(resolve(root, 'package-lock.json'), 'utf8'));
const sections = ['Smilelab npm dependency notices\n\n本文件包含工作台及构建工具的锁定依赖许可，不等同于运行时最小 SBOM。\n由 tools/generate-third-party-notices.mjs 从已安装包生成。\n'];
const missing = [];
for (const [location, entry] of Object.entries(lock.packages).sort(([a], [b]) => a.localeCompare(b))) {
  if (!location.startsWith('node_modules/') || entry.link) continue;
  const folder = resolve(root, location);
  if (!folder.startsWith(resolve(root, 'node_modules') + '\\') && !folder.startsWith(resolve(root, 'node_modules') + '/')) throw new Error('Invalid dependency path');
  if (!existsSync(resolve(folder, 'package.json'))) {
    if (!entry.optional) throw new Error('Required dependency not installed: ' + location);
    continue;
  }
  const metadata = JSON.parse(readFileSync(resolve(folder, 'package.json'), 'utf8'));
  const files = readdirSync(folder, { withFileTypes: true }).filter(item => item.isFile() && /^(licen[sc]e|copying|notice)(\.|$|-)/i.test(item.name)).map(item => item.name).sort();
  sections.push(`\n${'='.repeat(72)}\n${metadata.name ?? location} ${metadata.version ?? entry.version}\nDeclared license: ${typeof metadata.license === 'string' ? metadata.license : JSON.stringify(metadata.license ?? entry.license ?? 'unspecified')}\n`);
  if (!files.length) {
    const supplement = metadata.name.startsWith('@ant-design/icons-') ? 'ant-design-icons' : metadata.name.startsWith('@rolldown/binding-') ? 'rolldown' : metadata.name === '@vue/devtools-api' ? 'vue-devtools' : metadata.name;
    const source = resolve(root, 'tools/third-party-licenses', supplement + '.txt');
    if (existsSync(source)) sections.push('\n--- Upstream license snapshot ---\n' + readFileSync(source, 'utf8') + '\n');
    else missing.push(metadata.name ?? location);
  }
  for (const file of files) sections.push(`\n--- ${file} ---\n${readFileSync(resolve(folder, file), 'utf8')}\n`);
}
sections.push('\nPackages without a top-level license text (see their package license metadata and repository):\n' + missing.join('\n') + '\n');
const output = resolve(root, 'backend/public/THIRD_PARTY_NOTICES.txt');
mkdirSync(dirname(output), { recursive: true });
writeFileSync(output, sections.join(''));
console.log(`Generated npm notices; ${missing.length} packages have no top-level license text.`);
