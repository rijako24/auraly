import { cpSync, existsSync, mkdirSync, rmSync } from "node:fs";
import { join } from "node:path";

const root = process.cwd();
const nextRoot = join(root, ".next");
const standaloneRoot = join(nextRoot, "standalone");

if (!existsSync(standaloneRoot)) {
  throw new Error("Next.js did not produce .next/standalone.");
}

function replaceDirectory(source, destination) {
  rmSync(destination, { recursive: true, force: true });
  mkdirSync(destination, { recursive: true });
  cpSync(source, destination, { recursive: true });
}

replaceDirectory(join(nextRoot, "static"), join(standaloneRoot, ".next", "static"));

const publicRoot = join(root, "public");
if (existsSync(publicRoot)) {
  replaceDirectory(publicRoot, join(standaloneRoot, "public"));
}

