import json
import sys
from pathlib import Path

from playwright.sync_api import sync_playwright

HERE = Path(__file__).resolve().parent
PROJECT = Path(sys.argv[1])
OUT = Path(sys.argv[2])
OUT.mkdir(parents=True, exist_ok=True)
CANVAS = json.loads((PROJECT / "canvas.json").read_text())

CASES = [
    ("Main.dc.html", {}, {}, "main"),
    ("Main.dc.html", {}, {}, "main-phone"),
    ("Terminal.dc.html", {"theme": "Dx Dark"}, {}, "terminal-dark"),
    ("Terminal.dc.html", {"theme": "Dx Paper"}, {}, "terminal-paper"),
    ("Terminal.dc.html", {"theme": "Phosphor"}, {}, "terminal-phosphor"),
    ("Terminal.dc.html", {"theme": "Dx Dark"}, {"tab": "actions"}, "terminal-actions"),
    ("Inspector.dc.html", {}, {}, "inspector"),
    ("ThemeRoles.dc.html", {}, {}, "themeroles-proposed"),
    ("ThemeRoles.dc.html", {}, {"fixed": True, "bright": False}, "themeroles-fixed"),
    ("Experiments.dc.html", {}, {}, "experiments"),
    ("Syntax.dc.html", {"theme": "Dx Dark"}, {}, "syntax-dark"),
    ("Syntax.dc.html", {"theme": "Dx Paper"}, {}, "syntax-paper"),
    ("Syntax.dc.html", {"theme": "Phosphor"}, {"gray": True}, "syntax-phosphor-gray"),
    ("QuickLaunch.dc.html", {"theme": "Dx Dark"}, {"mode": "open"}, "ql-open"),
    ("QuickLaunch.dc.html", {"theme": "Dx Dark"}, {"mode": "search"}, "ql-search"),
    ("QuickLaunch.dc.html", {"theme": "Dx Paper"}, {"mode": "arg"}, "ql-arg-paper"),
    ("QuickLaunch.dc.html", {"theme": "Phosphor"}, {"mode": "err"}, "ql-err-phosphor"),
    ("Roadmap.dc.html", {}, {}, "roadmap"),
]

harness = (HERE / "harness.js").read_text()
report = {}
with sync_playwright() as p:
    browser = p.chromium.launch(args=["--disable-dev-shm-usage", "--no-sandbox", "--disable-gpu"])
    for name, props, state, slug in CASES:
        if only := sys.argv[3:]:
            if slug not in only:
                continue
        board = CANVAS["boards"][name]
        w, h = board["w"], board["h"]
        if slug.endswith("phone"):
            w = 390
        page = browser.new_page(viewport={"width": w, "height": 800})
        page.set_content("<!doctype html><html><head><meta charset='utf-8'></head><body style='margin:0'><div id='root'></div></body></html>")
        page.add_script_tag(content=harness)
        page.evaluate("([s,p,st]) => renderDc(s,p,st)", [(PROJECT / name).read_text(), props, state])
        try:
            page.wait_for_load_state("networkidle", timeout=8000)
        except Exception:
            pass
        page.evaluate("document.fonts.ready")
        frame_h = h if not slug.endswith("phone") else 100000
        issues = page.evaluate("([w,h]) => auditDc(w,h)", [w, frame_h])
        page.set_viewport_size({"width": w, "height": max(h, issues["contentHeight"]) if not slug.endswith("phone") else issues["contentHeight"]})
        page.screenshot(path=str(OUT / f"{slug}.png"), full_page=True)
        report[slug] = issues
        page.close()
    browser.close()
print(json.dumps(report, indent=1, ensure_ascii=False))
