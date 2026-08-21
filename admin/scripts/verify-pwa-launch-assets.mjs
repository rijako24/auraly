import { access, readFile } from "node:fs/promises";
import path from "node:path";
import sharp from "sharp";

const root = process.cwd();
const expectedScreens = [
  [750, 1334], [828, 1792], [1125, 2436], [1170, 2532],
  [1179, 2556], [1242, 2688], [1284, 2778], [1290, 2796],
];

for (const [width, height] of expectedScreens) {
  const file = path.join(root, "public", "brand", "launch", `auraly-${width}x${height}-v4.png`);
  await access(file);
  const image = sharp(file);
  const metadata = await image.metadata();
  if (metadata.width !== width || metadata.height !== height)
    throw new Error(`${path.basename(file)} has ${metadata.width}x${metadata.height}; expected ${width}x${height}.`);
  const { data, info } = await image.raw().toBuffer({ resolveWithObject: true });
  const pixel = (x, y) => Array.from(data.subarray((y * info.width + x) * info.channels, (y * info.width + x + 1) * info.channels));
  for (const [x, y] of [[0, 0], [width - 1, 0], [0, height - 1], [width - 1, height - 1]]) {
    const channels = pixel(x, y);
    if (channels.slice(0, 3).some(channel => channel < 220) || (channels[3] !== undefined && channels[3] !== 255))
      throw new Error(`${path.basename(file)} has a dark or transparent launch corner at ${x},${y}: ${channels.join(",")}.`);
  }
}

const manifest = JSON.parse(await readFile(path.join(root, "public", "app.webmanifest"), "utf8"));
if (manifest.display !== "standalone" || manifest.background_color !== "#f8fafc" || manifest.theme_color !== "#f8fafc")
  throw new Error("The app manifest must use standalone mode and the light launch background.");
for (const icon of manifest.icons) await access(path.join(root, "public", icon.src.replace(/^\//, "")));

console.log(`Verified ${expectedScreens.length} opaque iOS launch screens and ${manifest.icons.length} Android PWA icons.`);
