import { execFile } from "node:child_process";
import { readFile } from "node:fs/promises";
import { extname, join } from "node:path";
import { promisify } from "node:util";
import { fileURLToPath } from "node:url";

const execFileAsync = promisify(execFile);
const projectRoot = fileURLToPath(new URL("..", import.meta.url));
const allowed = new Set([".ts", ".tsx", ".js", ".jsx", ".json", ".css", ".md"]);
const markers = [
  String.fromCodePoint(0x00c3),
  String.fromCodePoint(0x00c2),
  String.fromCodePoint(0xfffd),
  String.fromCodePoint(0x00e2, 0x20ac),
  String.fromCodePoint(0x00f0, 0x0178),
];

const { stdout } = await execFileAsync(
  "git",
  ["ls-files", "--cached", "--others", "--exclude-standard", "--", "src", "e2e"],
  { cwd: projectRoot },
);
const files = stdout
  .split(String.fromCodePoint(10))
  .map((file) => file.replace(String.fromCodePoint(13), ""))
  .filter(Boolean)
  .filter((file) => allowed.has(extname(file)));

const failures = (
  await Promise.all(
    files.map(async (file) => {
      const content = await readFile(join(projectRoot, file), "utf8");
      return markers.some((marker) => content.includes(marker)) ? file : null;
    }),
  )
).filter(Boolean);

if (failures.length) {
  console.error("Damaged UTF-8 text was detected:");
  for (const file of failures) console.error(` - ${file}`);
  process.exit(1);
}
console.log("UTF-8 source encoding verified.");