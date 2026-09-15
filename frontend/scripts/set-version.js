const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

let sha = process.env.GIT_SHA;
if (!sha) {
  try {
    sha = execSync('git rev-parse --short HEAD').toString().trim();
  } catch {
    // git unavailable in this build environment (e.g. some Docker stages)
    sha = 'unknown';
  }
}

const outPath = path.join(__dirname, '../src/environments/version.ts');
fs.writeFileSync(outPath, `export const APP_SHA = '${sha}';\n`);
