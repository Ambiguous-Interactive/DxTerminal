import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const workflowPath = path.join(repoRoot, ".github/workflows/docs-build.yml");
const workflow = fs.readFileSync(workflowPath, "utf8").replaceAll("\r\n", "\n");

function job(name) {
  const start = workflow.indexOf(`\n  ${name}:\n`);
  assert.notEqual(start, -1);
  return workflow.slice(start + 1).split(/\n(?=  [a-z][a-z-]*:\n)/)[0];
}

test("Pages deployment depends on a completed build and uses the Pages environment", () => {
  assert.match(workflow, /^  workflow_dispatch:$/m);
  const deploy = job("deploy");
  assert.match(deploy, /^    needs: docs-build$/m);
  assert.match(deploy, /^    environment: github-pages$/m);
});

test("Pages actions are SHA-pinned with least privilege", () => {
  const build = job("docs-build");
  const deploy = job("deploy");
  assert.match(build, /actions\/upload-pages-artifact@fc324d3547104276b827a68afc52ff2a11cc49c9/);
  assert.match(deploy, /actions\/deploy-pages@368f82528645a54fb793d4d04e342629a3f51346/);
  assert.match(deploy, /^      pages: write$/m);
  assert.match(deploy, /^      id-token: write$/m);
  assert.doesNotMatch(deploy, /^      contents: write$/m);
  assert.doesNotMatch(deploy, /^      attestations: write$/m);
});

test("Pages fetch, upload and deployment require an explicit licensed run on the release branch", () => {
  const steps = job("docs-build").split(/^      - name: /m);
  const scopes = [job("deploy")];
  for (const action of ["gh run download", "actions/upload-pages-artifact@"]) {
    const step = steps.find((section) => section.includes(action));
    assert.ok(step, `missing the ${action} step`);
    scopes.push(step);
  }
  const release = {
    "github.event_name": "workflow_dispatch",
    "github.repository": "wallstop/DxCommandTerminal",
    "github.ref": "refs/heads/master",
    "inputs.licensed_run_id": "12345",
  };
  const cases = [
    [release, true],
    [{ ...release, "inputs.licensed_run_id": "" }, false],
    [{ ...release, "inputs.licensed_run_id": undefined }, false],
    [{ ...release, "github.event_name": "push" }, false],
    [{ ...release, "github.event_name": "pull_request" }, false],
    [{ ...release, "github.repository": "another/repository" }, false],
    [{ ...release, "github.ref": "refs/heads/feature" }, false],
  ];
  for (const scope of scopes) {
    const guard = scope.match(/^ +if: (.+)$/m)?.[1];
    assert.ok(guard, `missing Pages guard in ${scope.split("\n")[0]}`);
    const comparisons = guard.split(/\s+&&\s+/).map((clause) => {
      const match = clause.match(/^(github\.[a-z_]+|inputs\.[a-z_]+) (==|!=) '([^']*)'$/);
      assert.ok(match && Object.hasOwn(release, match[1]), `unexpected guard comparison: ${clause}`);
      return match;
    });
    for (const [values, expected] of cases) {
      const enabled = comparisons.every(([, key, operator, literal]) => {
        const equal = (values[key] ?? "") === literal;
        return operator === "==" ? equal : !equal;
      });
      assert.equal(enabled, expected, `${guard} with ${JSON.stringify(values)}`);
    }
  }
  const preview = steps.find((step) => step.includes("name: docs-guides"));
  assert.ok(preview && !/^ +if:/m.test(preview), "guide previews must work without a licensed run id");
});
