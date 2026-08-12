import { opendir, readFile } from "node:fs/promises";
import { extname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const projectRoot = fileURLToPath(new URL("..", import.meta.url));
const allowed = new Set([".ts", ".tsx", ".js", ".jsx", ".json", ".css", ".md"]);
const markers = [
  String.fromCodePoint(0x00c3),
  String.fromCodePoint(0x00c2),
  String.fromCodePoint(0xfffd),
  String.fromCodePoint(0x00e2, 0x20ac),
  String.fromCodePoint(0x00f0, 0x0178),
];

async function collectFiles(directory) {
  const files = [];
  const entries = await opendir(directory);
  for await (const entry of entries) {
    const absolutePath = join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...(await collectFiles(absolutePath)));
    } else if (entry.isFile() && allowed.has(extname(entry.name))) {
      files.push(absolutePath);
    }
  }
  return files;
}

const roots = ["src", "e2e"];
const files = (
  await Promise.all(roots.map((root) => collectFiles(join(projectRoot, root))))
).flat();

const failures = (
  await Promise.all(
    files.map(async (absolutePath) => {
      const source = await readFile(absolutePath, "utf8");
      return markers.some((marker) => source.includes(marker))
        ? relative(projectRoot, absolutePath)
        : null;
    }),
  )
).filter(Boolean);

if (failures.length) {
  console.error("Damaged UTF-8 text was detected:");
  for (const file of failures) console.error(` - ${file}`);
  process.exit(1);
}
console.log("UTF-8 source encoding verified.");
