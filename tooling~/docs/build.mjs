/*
    Full API extraction uses the licensed 2021.3 runner's installed engine
    modules. DocFX resolves its reference glob inside this project, so stage
    just those DLLs for extraction and remove them before rendering the site.
    --guides-only renders guides on a standalone checkout and reuses any
    locally generated API model.

    Input-system-only APIs are excluded because this source extraction uses
    the default compilation symbols. DocFX tolerates source compilation
    errors, so the full build verifies core public API pages explicitly.
*/
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { SCREENSHOT_GROUPS } from "../scripts/t11/scenarios.mjs";

const GUIDES_ONLY = process.argv.includes("--guides-only");
const docsDir = path.dirname(fileURLToPath(import.meta.url));
const objDir = path.join(docsDir, "obj");
const refsDir = path.join(objDir, "unity-refs");
const guidesDir = path.join(objDir, "guides");
const samplesDir = path.join(guidesDir, "samples");
const repoRoot = path.resolve(docsDir, "..", "..");
const GUIDES_SOURCE = path.join(repoRoot, "Documentation~");
const SAMPLES_SOURCE = path.join(repoRoot, "Samples~", "TerminalCommands");
const CATALOG_SOURCE = path.join(repoRoot, "tooling~", "scripts/t11/scenario-catalog.json");
const BASELINES_SOURCE = path.join(repoRoot, "Tests/Runtime/Capture/Baselines~");
const SCREENSHOT_GROUP_ORDER = SCREENSHOT_GROUPS.filter((group) => group !== "negative");
const PROVENANCE = (scenario, field) => {
  const value = scenario[field];
  return value === null || value === undefined ? "—" : `\`${value}\``;
};

/*
    ManagedReference pages are one file per type, named exactly by uid. A
    missing or degenerate page here means API extraction degraded silently
    (the failure mode allowCompilationErrors permits), so the build must
    fail.
*/
const REQUIRED_API_PAGES = [
  "WallstopStudios.DxCommandTerminal.Backend.Terminal",
  "WallstopStudios.DxCommandTerminal.Backend.CommandShell",
  "WallstopStudios.DxCommandTerminal.Backend.CommandArg",
  "WallstopStudios.DxCommandTerminal.Backend.CommandBuilder",
  "WallstopStudios.DxCommandTerminal.Backend.CommandInfo",
  "WallstopStudios.DxCommandTerminal.Backend.CommandAutoComplete",
  "WallstopStudios.DxCommandTerminal.Attributes.RegisterCommandAttribute",
  "WallstopStudios.DxCommandTerminal.UI.TerminalUI",
  "WallstopStudios.DxCommandTerminal.UI.CommandPaletteUI",
  "WallstopStudios.DxCommandTerminal.Themes.TerminalThemePack",
  "WallstopStudios.DxCommandTerminal.Themes.TerminalFontPack",
];

function missingRequiredApiPages() {
  return REQUIRED_API_PAGES.filter((uid) => {
    const page = path.join(objDir, "api", `${uid}.yml`);
    if (!fs.existsSync(page)) {
      return true;
    }
    return !fs.readFileSync(page, "utf8").includes("items:");
  });
}

function run(command, args, cwd = docsDir) {
  execFileSync(command, args, {
    stdio: "inherit",
    cwd,
    shell: false,
  });
}

function copyReferenceDlls() {
  const sourceDir = process.env.UNITY_EDITOR_MANAGED;
  if (!sourceDir || !fs.existsSync(path.join(sourceDir, "UnityEngine.CoreModule.dll"))) {
    throw new Error("Set UNITY_EDITOR_MANAGED to the installed Unity 2021.3 engine modules for docs:build. Use npm --prefix tooling~ run docs:guides for a standalone preview.");
  }
  fs.rmSync(refsDir, { recursive: true, force: true });
  fs.mkdirSync(refsDir, { recursive: true });
  const dlls = fs.readdirSync(sourceDir).filter((file) => /^UnityEngine.*\.dll$/u.test(file));
  for (const file of dlls) {
    fs.copyFileSync(path.join(sourceDir, file), path.join(refsDir, file));
  }
  return dlls.length;
}

/*
    The guides live in Documentation~ and docfx cannot read anything outside
    its project directory (no `../` in content, snippet, or reference
    paths), so the build stages every input inside obj/: guide markdown,
    the real sample sources ([!code-csharp] excerpts stay byte-identical to
    the shipped samples, so docs cannot drift), and the Unity reference
    assemblies the API extraction binds against.
*/
function copyGuides() {
  fs.rmSync(guidesDir, { recursive: true, force: true });
  fs.mkdirSync(guidesDir, { recursive: true });
  const files = fs.readdirSync(GUIDES_SOURCE).filter((file) => file.endsWith(".md"));
  const hasApi = fs.existsSync(path.join(objDir, "api", "toc.yml"));
  for (const file of files) {
    const text = fs.readFileSync(path.join(GUIDES_SOURCE, file), "utf8");
    // A fresh standalone preview can identify the referenced types, while
    // links to the generated API are available with a local metadata model.
    const markdown = hasApi ? text : text
      .replace(/\[([^\]]+)\]\(xref:([^)]+)\)/gu, "$1 (`$2`)")
      .replace(/<xref:([^>]+)>/gu, "`$1`");
    fs.writeFileSync(path.join(guidesDir, file), markdown);
  }
  const toc = fs.readFileSync(path.join(docsDir, "toc.yml"), "utf8");
  fs.writeFileSync(path.join(guidesDir, "toc.yml"), hasApi ? toc : toc.replace(/^- name: API Reference\r?\n  href: \.\.\/api\/toc\.yml\r?\n?/mu, ""));
  return files.length;
}

function copySampleSources() {
  fs.mkdirSync(samplesDir, { recursive: true });
  const files = fs
    .readdirSync(SAMPLES_SOURCE)
    .filter((file) => file.endsWith(".cs"));
  for (const file of files) {
    fs.copyFileSync(path.join(SAMPLES_SOURCE, file), path.join(samplesDir, file));
  }
  return files.length;
}

/*
    The screenshots page is generated, never hand-written: it renders the
    scenario catalog (tooling~/scripts/t11/scenario-catalog.json) against the
    committed T11 baseline store, so docs imagery and the visual-test fixtures
    cannot drift apart. Every baselined scenario gets its pinned/variant PNG
    with its provenance line; negative (never-baselined) scenarios are listed
    without imagery. `npm run t4:capture` regenerates every image shown here.
    Throws when a cataloged scenario has no committed baseline PNG - lint:docs-
    catalog reports that staleness too, but the docs build must not publish a
    catalog it cannot render.
*/
function generateScreenshotsPage() {
  const catalog = JSON.parse(fs.readFileSync(CATALOG_SOURCE, "utf8"));
  const environments = fs
    .readdirSync(BASELINES_SOURCE, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => {
      const index = JSON.parse(
        fs.readFileSync(path.join(BASELINES_SOURCE, entry.name, "index.json"), "utf8")
      );
      return { name: entry.name, index };
    });
  const pinned = environments.filter(
    ({ index }) => index.environment?.pinned !== false
  );
  const environmentFor = (name) =>
    pinned.find(({ index }) => Object.hasOwn(index.scenarios, name)) ??
    environments.find(({ index }) => Object.hasOwn(index.scenarios, name));
  const environmentLabel = ({ index }) => {
    const { unityVersion, graphicsApi, colorSpace, resolution, logicalScale } =
      index.environment;
    return `${unityVersion} · ${graphicsApi} · ${colorSpace} · ${resolution.width}x${resolution.height} @ ${logicalScale}x`;
  };

  const imagesDir = path.join(guidesDir, "images");
  fs.rmSync(imagesDir, { recursive: true, force: true });
  fs.mkdirSync(imagesDir, { recursive: true });

  const grouped = new Map();
  const negatives = [];
  let copied = 0;
  for (const [name, entry] of Object.entries(catalog.scenarios)) {
    if (entry.baselined === false) {
      negatives.push([name, entry]);
      continue;
    }
    const env = environmentFor(name);
    if (env === undefined) {
      throw new Error(`scenario ${name} has no committed baseline to render`);
    }
    const scenario = env.index.scenarios[name];
    const source = path.join(BASELINES_SOURCE, env.name, scenario.png);
    if (!fs.existsSync(source)) {
      throw new Error(`scenario ${name} baseline PNG is missing: ${source}`);
    }
    fs.copyFileSync(source, path.join(imagesDir, `${name}.png`));
    copied++;
    const bucket = grouped.get(entry.group) ?? [];
    bucket.push({ name, entry, scenario, environment: environmentLabel(env) });
    grouped.set(entry.group, bucket);
  }

  const lines = [
    "# Screenshots",
    "",
    "Every surface the visual harness pins: the T04 capture fixtures render each",
    "scenario on the pinned Unity host, and the T11 golden baselines keep these",
    "images byte-stable per environment. Regenerate every image with one command:",
    "`npm run t4:capture` (captures, validates manifests, and promotes baselines",
    "through the T11 gate).",
    "",
    "<!-- generated by tooling~/docs/build.mjs from scenario-catalog.json; do not edit -->",
    ""
  ];
  for (const group of SCREENSHOT_GROUP_ORDER) {
    const scenarios = grouped.get(group);
    if (scenarios === undefined) continue;
    lines.push(`## ${group}`, "");
    for (const scenario of scenarios) {
      lines.push(
        `### ${scenario.name}`,
        "",
        scenario.entry.summary,
        "",
        `![${scenario.name}](images/${scenario.name}.png)`,
        "",
        `\`${scenario.environment}\` · captured ${scenario.scenario.capturedUtc} · ` +
          `theme ${PROVENANCE(scenario.scenario, "theme")} · font ${PROVENANCE(scenario.scenario, "font")}`,
        ""
      );
    }
  }
  const leftoverGroups = [...grouped.keys()].filter(
    (group) => !SCREENSHOT_GROUP_ORDER.includes(group)
  );
  if (leftoverGroups.length > 0) {
    throw new Error(
      `catalog groups not rendered on the screenshots page: ${leftoverGroups.join(", ")}` +
        ` (add them to SCREENSHOT_GROUPS in tooling~/scripts/t11/scenarios.mjs)`
    );
  }
  if (negatives.length > 0) {
    lines.push("## negative controls", "");
    for (const [name, entry] of negatives) {
      lines.push(`- **${name}** - ${entry.summary}`);
    }
    lines.push("");
  }
  fs.writeFileSync(path.join(guidesDir, "screenshots.md"), lines.join("\n"));
  return { rendered: copied, negatives: negatives.length };
}

let dllCount = 0;
run("dotnet", ["tool", "restore"], repoRoot);
if (!GUIDES_ONLY) {
  try {
    dllCount = copyReferenceDlls();
    fs.rmSync(path.join(objDir, "api"), { recursive: true, force: true });
    run("dotnet", ["docfx", "metadata", "docfx.json"]);
  } finally {
    fs.rmSync(refsDir, { recursive: true, force: true });
  }
  const missingPages = missingRequiredApiPages();
  if (missingPages.length > 0) {
    throw new Error(
      `API extraction dropped ${missingPages.length} required page(s) ` +
        `(docfx source mode fails silently; see REQUIRED_API_PAGES):\n` +
        missingPages.join("\n")
    );
  }
}
const guideCount = copyGuides();
const sampleCount = copySampleSources();
const screenshots = generateScreenshotsPage();
fs.rmSync(path.join(objDir, "_site"), { recursive: true, force: true });
run("dotnet", ["docfx", "build", "docfx.json"]);

const siteDir = path.join(objDir, "_site");
const pages = fs.readdirSync(siteDir).filter((file) => file.endsWith(".html")).length;
console.log(
  `[docs] ok: ${GUIDES_ONLY ? "guides-only" : `${dllCount} reference DLLs`}, ${guideCount} guides, ` +
    `${sampleCount} sample sources, ${screenshots.rendered} screenshots (+${screenshots.negatives} negative controls), ` +
    `site at ${path.relative(process.cwd(), siteDir)} (${pages} root pages)`
);
