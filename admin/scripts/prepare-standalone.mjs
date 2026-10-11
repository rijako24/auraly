import { cpSync, existsSync, mkdirSync, rmSync } from "node:fs";
import { isAbsolute, join, relative, resolve } from "node:path";

const root = process.cwd();
const nextRoot = resolve(root, process.env.AURALY_NEXT_DIST_DIR || ".next");
const relativeDist = relative(root, nextRoot);
if (!relativeDist || relativeDist === ".." || relativeDist.startsWith(`..\\`) ||
    relativeDist.startsWith("../") || isAbsolute(relativeDist)) {
  throw new Error("The Next.js output directory must stay inside the admin workspace.");
}
const standaloneRoot = join(nextRoot, "standalone");

if (!existsSync(standaloneRoot)) {
  throw new Error("Next.js did not produce .next/standalone.");
}

function replaceDirectory(source, destination) {
  rmSync(destination, { recursive: true, force: true });
  mkdirSync(destination, { recursive: true });
  cpSync(source, destination, { recursive: true });
}

replaceDirectory(join(nextRoot, "static"), join(standaloneRoot, relativeDist, "static"));

const publicRoot = join(root, "public");
if (existsSync(publicRoot)) {
  replaceDirectory(publicRoot, join(standaloneRoot, "public"));
}

