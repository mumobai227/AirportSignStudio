// Independent browser rendering of exported SVGs; compares with the C# PNGs.
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.argv[2]);
(async () => {
  const root = path.resolve(__dirname, '..', 'validation_v3_1');
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    for (const name of ['v3_1_merged_atlas']) {
      const svg = fs.readFileSync(path.join(root, name + '.svg'), 'utf8');
      const [, w, h] = svg.match(/viewBox="0 0 ([\d.]+) ([\d.]+)"/).map(Number);
      const scale = 1;
      const width = w, height = h;
      const page = await browser.newPage({ viewport: { width, height }, deviceScaleFactor: 1 });
      await page.route('**/*', route => route.abort());
      await page.setContent(`<style>body{margin:0;background:transparent}svg{position:absolute;left:${(width-w*scale)/2}px;top:${(height-h*scale)/2}px;width:${w*scale}px;height:${h*scale}px}</style>${svg}`);
      await page.screenshot({ path: path.join(root, name + '-svg-browser.png'), omitBackground: true });
      await page.close();
      console.log('Rendered SVG:', name, width, height);
    }
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
