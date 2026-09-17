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

// CI computes this from version.json via `nbgv get-version` (git height as
// the real, orderable patch component) — see .github/workflows/*.yml. Falls
// back to the static package.json version for local/unbuilt-by-CI runs.
const version = process.env.APP_VERSION || require('../package.json').version;

const outPath = path.join(__dirname, '../src/environments/version.ts');
fs.writeFileSync(
  outPath,
  `export const APP_SHA = '${sha}';\nexport const APP_VERSION = '${version}';\n`,
);
