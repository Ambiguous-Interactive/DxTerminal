import json, sys
from pathlib import Path
from playwright.sync_api import sync_playwright
HERE = Path(__file__).resolve().parent
P = Path(sys.argv[1]); OUT = Path(sys.argv[2]); TXT = Path(sys.argv[3])
C = json.loads((P / "canvas.json").read_text())
h = (HERE / "harness.js").read_text()
with sync_playwright() as p:
    b = p.chromium.launch(args=["--disable-dev-shm-usage", "--no-sandbox", "--disable-gpu"])
    for name in C["order"]:
        bd = C["boards"][name]
        pg = b.new_page(viewport={"width": bd["w"], "height": 800})
        pg.set_content("<!doctype html><html><head><meta charset='utf-8'></head><body style='margin:0;background:#0D1117'><div id='root'></div></body></html>")
        pg.add_script_tag(content=h)
        pg.evaluate("([s]) => renderDc(s,{},{})", [(P / name).read_text()])
        pg.evaluate("document.fonts.ready")
        html = pg.evaluate("() => { document.querySelectorAll('script').forEach(s => s.remove()); return '<!doctype html>\\n' + document.documentElement.outerHTML; }")
        stem = name.replace(".dc.html", "")
        (OUT / f"{stem}.html").write_text(html)
        (TXT / f"{stem}.txt").write_text(pg.evaluate("document.getElementById('root').innerText"))
        pg.close()
    b.close()
