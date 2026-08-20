import { mkdir, readFile } from "node:fs/promises";
import path from "node:path";
import sharp from "sharp";

const root = process.cwd();
const source = path.join(root, "public", "brand", "auraly-app-icon-512.png");
const symbolSource = path.join(root, "public", "brand", "auraly-symbol.png");
const destination = path.join(root, "public", "brand", "launch");
const screens = [
  [750, 1334],
  [828, 1792],
  [1125, 2436],
  [1170, 2532],
  [1179, 2556],
  [1242, 2688],
  [1284, 2778],
  [1290, 2796],
];

await mkdir(destination, { recursive: true });
const icon = await readFile(source);
const symbol = await readFile(symbolSource);

for (const [width, height] of screens) {
  const iconSize = Math.round(Math.min(width * 0.36, 430));
  const launchScreen = Buffer.from(`
    <svg width="${width}" height="${height}" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <radialGradient id="halo" cx="50%" cy="42%" r="58%">
          <stop offset="0" stop-color="#d9f7f2" stop-opacity=".78"/>
          <stop offset="1" stop-color="#f8fafc" stop-opacity="0"/>
        </radialGradient>
      </defs>
      <rect width="100%" height="100%" fill="#f8fafc"/>
      <circle cx="${width * 0.9}" cy="${height * 0.07}" r="${width * 0.3}" fill="#dff8f4"/>
      <circle cx="${-width * 0.05}" cy="${height * 0.88}" r="${width * 0.34}" fill="#e3f3f1"/>
      <circle cx="${width * 0.98}" cy="${height * 0.7}" r="${width * 0.12}" fill="#eef9f7"/>
      <circle cx="${width / 2}" cy="${height * 0.4}" r="${width * 0.55}" fill="url(#halo)"/>
      <text x="50%" y="${height * 0.565}" text-anchor="middle" font-family="Arial, Helvetica, sans-serif" font-size="${Math.round(width * 0.031)}" font-weight="600" letter-spacing="${Math.round(width * 0.003)}" fill="#52706f">Tu operación, siempre en movimiento</text>
    </svg>`);
  const transparentSymbol = await sharp(symbol).resize({ width: iconSize }).png().toBuffer();
  const symbolHeight = (await sharp(transparentSymbol).metadata()).height ?? iconSize;
  await sharp(launchScreen)
    .composite([{ input: transparentSymbol, left: Math.round((width - iconSize) / 2), top: Math.round(height * 0.465 - symbolHeight / 2) }])
    .png({ compressionLevel: 9, palette: true, quality: 92 })
    .toFile(path.join(destination, `auraly-${width}x${height}.png`));
}

await sharp(icon).resize(512, 512).png({ compressionLevel: 9 }).toFile(path.join(root, "public", "brand", "auraly-ios-icon-512.png"));
