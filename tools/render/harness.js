window.renderDc = function (src, propOverrides, stateOverrides) {
  const doc = new DOMParser().parseFromString(src, 'text/html');
  const scriptEl = doc.querySelector('script[data-dc-script]');
  const propsSpec = JSON.parse(scriptEl.getAttribute('data-props') || '{}');
  const props = {};
  for (const k of Object.keys(propsSpec)) {
    if (k[0] !== '$' && propsSpec[k] && 'default' in propsSpec[k]) props[k] = propsSpec[k].default;
  }
  Object.assign(props, propOverrides || {});
  const preview = propsSpec.$preview || { width: 1280, height: 720 };
  class DCLogic {
    constructor(p) { this.props = p; this.state = {}; }
    setState(p) { Object.assign(this.state, p); draw(); }
    forceUpdate() { draw(); }
  }
  const Component = new Function('DCLogic', scriptEl.textContent + '\nreturn Component;')(DCLogic);
  const inst = new Component(props);
  Object.assign(inst.state, stateOverrides || {});
  const xdc = doc.querySelector('x-dc');
  const helmet = xdc.querySelector('helmet');
  if (helmet) {
    for (const n of Array.from(helmet.children)) document.head.appendChild(n.cloneNode(true));
    helmet.remove();
  }
  const HOLE = /\{\{\s*([^}]+?)\s*\}\}/g;
  function lookup(path, scopes) {
    path = path.trim();
    if (path === 'true') return true;
    if (path === 'false') return false;
    if (/^-?\d+(\.\d+)?$/.test(path)) return Number(path);
    const parts = path.split('.');
    for (let i = scopes.length - 1; 0 <= i; i--) {
      if (parts[0] in scopes[i]) {
        let v = scopes[i][parts[0]];
        for (let j = 1; j < parts.length; j++) v = v == null ? undefined : v[parts[j]];
        return v;
      }
    }
    return undefined;
  }
  function interp(s, scopes) {
    return s.replace(HOLE, (_, p) => { const v = lookup(p, scopes); return v == null ? '' : String(v); });
  }
  function expand(node, scopes, out) {
    if (node.nodeType === 3) { out.appendChild(document.createTextNode(interp(node.textContent, scopes))); return; }
    if (node.nodeType !== 1) return;
    const tag = node.tagName.toLowerCase();
    if (tag === 'sc-for') {
      const m = /^\s*\{\{\s*([^}]+?)\s*\}\}\s*$/.exec(node.getAttribute('list') || '');
      const list = m ? lookup(m[1], scopes) : [];
      const as = node.getAttribute('as') || 'item';
      (list || []).forEach((item, i) => {
        const sc = {}; sc[as] = item; sc.$index = i;
        for (const c of node.childNodes) expand(c, scopes.concat([sc]), out);
      });
      return;
    }
    if (tag === 'sc-if') {
      const m = /^\s*\{\{\s*([^}]+?)\s*\}\}\s*$/.exec(node.getAttribute('value') || '');
      if (m && lookup(m[1], scopes)) for (const c of node.childNodes) expand(c, scopes, out);
      return;
    }
    const isSvg = node.namespaceURI === 'http://www.w3.org/2000/svg';
    const el = isSvg ? document.createElementNS('http://www.w3.org/2000/svg', tag) : document.createElement(tag);
    for (const a of Array.from(node.attributes)) {
      const whole = /^\s*\{\{\s*([^}]+?)\s*\}\}\s*$/.exec(a.value);
      if (/^on[A-Z]/.test(a.name) || /^on[a-z]/.test(a.name) && whole) {
        const fn = whole ? lookup(whole[1], scopes) : null;
        if (typeof fn === 'function') el.addEventListener(a.name.slice(2).toLowerCase(), fn);
        continue;
      }
      if (a.name.startsWith('hint-')) continue;
      el.setAttribute(a.name, interp(a.value, scopes));
    }
    const kids = tag === 'template' ? node.content.childNodes : node.childNodes;
    for (const c of kids) expand(c, scopes, el);
    out.appendChild(el);
  }
  const host = document.getElementById('root');
  function draw() {
    const vals = inst.renderVals();
    host.innerHTML = '';
    const frag = document.createDocumentFragment();
    for (const c of xdc.childNodes) expand(c, [vals], frag);
    host.appendChild(frag);
  }
  draw();
  return preview;
};

window.auditDc = function (frameW, frameH) {
  const root = document.getElementById('root');
  const parse = (c) => { const m = c.match(/[\d.]+/g); return m ? m.map(Number) : [0, 0, 0, 0]; };
  const lin = (v) => { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
  const lum = (r) => 0.2126 * lin(r[0]) + 0.7152 * lin(r[1]) + 0.0722 * lin(r[2]);
  const ratio = (a, b) => { const x = lum(a), y = lum(b); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); };
  function bgOf(el) {
    const stack = [];
    let e = el;
    while (e && e !== document.documentElement) {
      const c = parse(getComputedStyle(e).backgroundColor);
      const a = c.length > 3 ? c[3] : 1;
      if (0 < a) stack.push([c[0], c[1], c[2], a]);
      if (a >= 1) break;
      e = e.parentElement;
    }
    let base = [255, 255, 255];
    for (let i = stack.length - 1; 0 <= i; i--) {
      const c = stack[i];
      base = [0, 1, 2].map((k) => c[k] * c[3] + base[k] * (1 - c[3]));
    }
    return base;
  }
  const issues = { lowContrast: [], clipped: [], tiny: [], overflowFrame: null, offFrame: [] };
  const rr = root.getBoundingClientRect();
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const seen = new Set();
  while (walker.nextNode()) {
    const t = walker.currentNode;
    if (!t.textContent.trim()) continue;
    const el = t.parentElement;
    if (seen.has(el)) continue;
    seen.add(el);
    const cs = getComputedStyle(el);
    if (cs.visibility === 'hidden' || cs.display === 'none') continue;
    const r = el.getBoundingClientRect();
    if (r.width === 0) continue;
    if (el.closest('[aria-hidden="true"]')) continue;
    const fg = parse(cs.color);
    const fa = fg.length > 3 ? fg[3] : 1;
    const bg = bgOf(el);
    const eff = [0, 1, 2].map((k) => fg[k] * fa + bg[k] * (1 - fa));
    const cr = ratio(eff, bg);
    const size = parseFloat(cs.fontSize);
    const bold = parseInt(cs.fontWeight, 10) >= 700;
    const need = (size >= 24 || (size >= 18.66 && bold)) ? 3 : 4.5;
    const label = t.textContent.trim().slice(0, 40);
    if (cr < need) issues.lowContrast.push(label + ' | ' + cr.toFixed(2) + ' < ' + need + ' | ' + cs.color);
    if (size < 11) issues.tiny.push(label + ' | ' + size + 'px');
    if (rr.bottom + 1 < r.bottom || rr.right + 1 < r.right) issues.offFrame.push(label + ' | bottom ' + Math.round(r.bottom - rr.top) + ' right ' + Math.round(r.right - rr.left));
  }
  for (const el of root.querySelectorAll('*')) {
    const cs = getComputedStyle(el);
    if ((cs.overflow === 'hidden' || cs.overflowX === 'hidden' || cs.textOverflow === 'ellipsis') && el.scrollWidth > el.clientWidth + 1 && el.textContent.trim()) {
      issues.clipped.push(el.textContent.trim().slice(0, 50) + ' | ' + el.scrollWidth + '>' + el.clientWidth);
    }
  }
  const first = root.firstElementChild;
  const h = first ? Math.max(first.scrollHeight, first.getBoundingClientRect().height) : 0;
  issues.contentHeight = Math.round(h);
  if (frameH + 1 < h) issues.overflowFrame = 'content ' + Math.round(h) + ' > frame ' + frameH;
  return issues;
};
