import json
import re
import sys
from pathlib import Path

VAR = re.compile(r"--([a-z0-9-]+)\s*:\s*rgba?\(([^)]*)\)")
CLASS = re.compile(r"\.([a-z0-9-]+)\s*\{([^}]*)\}", re.S)

PAIRS = [
    ("text-message", "terminal-bg"),
    ("text-warning", "terminal-bg"),
    ("text-error", "terminal-bg"),
    ("text-input-echo", "terminal-bg"),
    ("text-shell", "terminal-bg"),
    ("input-text-color", "input-field-bg"),
    ("button-text", "button-bg"),
    ("button-selected-text", "button-selected-bg"),
    ("button-hover-text", "button-hover-bg"),
]
BACKDROPS = {"black": (0, 0, 0), "white": (255, 255, 255), "mid": (128, 128, 128)}


def parse(v):
    p = [float(x) for x in v.split(",")]
    return (p[0], p[1], p[2], p[3] if len(p) > 3 else 1.0)


def over(fg, bg):
    a = fg[3]
    return tuple(fg[i] * a + bg[i] * (1 - a) for i in range(3))


def lin(c):
    c /= 255
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lum(rgb):
    r, g, b = (lin(x) for x in rgb)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def wcag(a, b):
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)


def apca(txt, bg):
    def y(rgb):
        r, g, b = ((x / 255) ** 2.4 for x in rgb)
        v = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b
        return v if v > 0.022 else v + (0.022 - v) ** 1.414

    yt, yb = y(txt), y(bg)
    if abs(yb - yt) < 0.0005:
        return 0.0
    if yb > yt:
        s = (yb**0.56 - yt**0.57) * 1.14
        return 0.0 if s < 0.1 else (s - 0.027) * 100
    s = (yb**0.65 - yt**0.62) * 1.14
    return 0.0 if s > -0.1 else (s + 0.027) * 100


def audit(path):
    out = []
    text = Path(path).read_text()
    for name, body in CLASS.findall(text):
        vars_ = {k: parse(v) for k, v in VAR.findall(body)}
        if "terminal-bg" not in vars_:
            continue
        worst = []
        for fg, bg in PAIRS:
            if fg not in vars_ or bg not in vars_:
                continue
            best_w, best_a = 99.0, 999.0
            for bd in BACKDROPS.values():
                surface = over(vars_["terminal-bg"], bd)
                back = over(vars_[bg], surface) if bg != "terminal-bg" else surface
                t = over(vars_[fg], back)
                best_w = min(best_w, wcag(t, back))
                best_a = min(best_a, abs(apca(t, back)))
            worst.append((fg, round(best_w, 2), round(best_a, 1)))
        out.append({"theme": name, "file": Path(path).name, "pairs": worst})
    return out


rows = []
for p in sys.argv[1:]:
    rows.extend(audit(p))
print(json.dumps(rows))
