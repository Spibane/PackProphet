#!/usr/bin/env python3
"""
A/B/C probe for the opaque iOS "Script error" in KNOWN-ISSUES.md.

The error fires at +0.0s with no cross-origin code loaded and no callback of the app's involved,
which leaves the import map the .NET SDK writes as the only candidate that fits: a map the browser
objects to is reported by spec as an exception with no script behind it -- no filename, no Error
object, from a queued task -- which is that report's exact shape. Its `integrity` key is a 2024
addition to the spec that older WebKit does not implement.

Three pages, identical except for the import map, and nothing else running on any of them: no
runtime, no CDN, no app code, only the error surface itself.

    A   the real import map, including `integrity`
    B   the same map with `integrity` removed
    C   no import map at all (the control)

    A throws, B and C do not     the `integrity` key is the cause
    A and B throw, C does not    import maps generally, not that key
    all three throw              the import map is innocent; something on every page does it
    none throw                   the cause is elsewhere in the app, and the probe is too small

Run it on the machine, load it on the phone over the LAN:

    python3 tools/DiagProbe/probe.py

diag.js is served from wwwroot rather than copied, so the probe always tests the real error
surface and there is no second copy to fall out of step with it.
"""
import http.server
import io
import json
import os
import re
import socketserver
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DIAG = os.path.normpath(os.path.join(HERE, '..', '..', 'src', 'PackProphet.App', 'wwwroot', 'js', 'diag.js'))
PORT = 8899

CSS = """
body{font:16px/1.55 -apple-system,BlinkMacSystemFont,sans-serif;margin:0;
  padding:1.5rem 1rem 15rem;background:#fbfaf8;color:#1a1a1a}
h1{font-size:1.2rem;margin:0 0 .75rem;line-height:1.3}
code{background:#eceae6;padding:.1rem .3rem;border-radius:.2rem;font-size:.9em}
.verdict{font-weight:600}
nav{margin:1.75rem 0 0;display:flex;gap:.6rem;flex-wrap:wrap}
nav a{padding:.7rem 1.1rem;background:#1a1a1a;color:#fff;border-radius:.45rem;
  text-decoration:none;font-size:.95rem}
nav a[aria-current]{background:#b3261e}
#diag{position:fixed;bottom:0;left:0;right:0;background:#210;color:#fdd;
  font:12px/1.4 ui-monospace,monospace;max-height:55vh;overflow:auto;z-index:99}
#diag .diag-bar{display:flex;gap:.5rem;padding:.4rem .5rem;background:#421;position:sticky;top:0}
#diag .diag-bar strong{margin-right:auto;color:#ff9c9c}
#diag .diag-row{padding:.35rem .5rem;border-bottom:1px solid #521;white-space:pre-wrap}
#diag .diag-copy{width:100%;min-height:6rem;background:#000;color:inherit;font:inherit;
  border:1px solid #521}
"""

PAGES = {
    'a': ('the real import map, unchanged',
          'The import map as the build writes it, <code>integrity</code> key and all '
          '({n} entries).'),
    'b': ('the same map, <code>integrity</code> removed',
          'Identical to A in every other respect. <span class="verdict">A shows the error and '
          'B does not &rarr; the <code>integrity</code> key is the cause.</span>'),
    'c': ('no import map at all',
          'The control. <span class="verdict">If this one shows the error too, the import map is '
          'innocent</span> and something that happens on every page is responsible.'),
}


def import_map(which):
    full = json.load(open(os.path.join(HERE, 'importmap.json')))
    if which == 'a':
        return full
    if which == 'b':
        return {k: v for k, v in full.items() if k != 'integrity'}
    return None


def page(which):
    title, blurb = PAGES[which]
    the_map = import_map(which)
    tag = f'<script type="importmap">{json.dumps(the_map)}</script>' if the_map is not None else ''
    entries = len(json.load(open(os.path.join(HERE, 'importmap.json'))).get('integrity', {}))
    nav = ''.join(
        f'<a href="/{k}"{" aria-current=\"page\"" if k == which else ""}>'
        f'{k.upper()}: {["with integrity", "no integrity", "no import map"][i]}</a>'
        for i, k in enumerate(['a', 'b', 'c']))
    return f"""<!DOCTYPE html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<title>Probe {which.upper()}</title><style>{CSS}</style>
{tag}</head><body>
<h1>Probe {which.upper()} &mdash; {title}</h1>
<p>{blurb.format(n=entries)}</p>
<p><strong>A red box at the bottom of this page means it reproduced the error.</strong>
Nothing else runs here: no runtime, no CDN, no app code &mdash; only the error surface.</p>
<p>Tap <strong>copy</strong> on the box, then paste the report. The report names its own version,
so a cached copy cannot be mistaken for a current one.</p>
<nav>{nav}</nav>
<script src="/diag.js"></script>
</body></html>"""


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        route = self.path.split('?')[0].rstrip('/') or '/a'

        if route == '/diag.js':
            return self.send(io.open(DIAG, encoding='utf-8').read(), 'text/javascript')
        which = route.lstrip('/').lower()
        if which in PAGES:
            return self.send(page(which), 'text/html')
        if route == '/':
            return self.send(page('a'), 'text/html')
        self.send_error(404)

    def send(self, body, mime):
        data = body.encode('utf-8')
        self.send_response(200)
        self.send_header('Content-Type', f'{mime}; charset=utf-8')
        self.send_header('Content-Length', str(len(data)))
        # Never let the phone answer one of these from cache. Comparing readings taken from
        # different versions of diag.js without knowing it already cost a round of this.
        self.send_header('Cache-Control', 'no-store, no-cache, must-revalidate, max-age=0')
        self.send_header('Pragma', 'no-cache')
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, fmt, *args):
        print(f'  {self.address_string()} {fmt % args}', flush=True)


def lan_addresses():
    """
    Every IPv4 address this machine answers on, private ones first.

    Not "the" address: a laptop on a dock has two, and the one carrying the DEFAULT ROUTE is the
    wired network, while the phone is on the Wi-Fi. Asking a UDP socket where it would send a
    packet returns the wired address, which the phone cannot route to at all -- and it looks like
    a broken server rather than the wrong URL. So print all of them and let the reader pick the
    one their phone is on; a private address is very nearly always that one, so those go first.
    """
    found = []
    try:
        out = subprocess.run(['ifconfig'], capture_output=True, text=True, timeout=5).stdout
        interface = None
        for line in out.splitlines():
            if line and not line[0].isspace():
                interface = line.split(':')[0]
            match = re.search(r'\binet (\d+\.\d+\.\d+\.\d+)', line)
            if match and not match.group(1).startswith('127.'):
                found.append((interface or '?', match.group(1)))
    except (OSError, subprocess.SubprocessError):
        pass

    def private(address):
        a, b = (int(x) for x in address.split('.')[:2])
        return a == 10 or (a == 172 and 16 <= b <= 31) or (a == 192 and b == 168)

    found.sort(key=lambda pair: not private(pair[1]))
    return found or [('loopback', '127.0.0.1')]


if __name__ == '__main__':
    if not os.path.exists(DIAG):
        sys.exit(f'diag.js not found at {DIAG}')
    socketserver.TCPServer.allow_reuse_address = True
    with socketserver.TCPServer(('0.0.0.0', PORT), Handler) as server:
        addresses = lan_addresses()
        print('Probe pages: /a, /b and /c on each of these. Use the one your PHONE is on --')
        print('a wired address the phone cannot route to looks exactly like a broken server.')
        for interface, address in addresses:
            print(f'    http://{address}:{PORT}/a    ({interface})')
        print('\nLoad each page in turn and note which show a red box. Ctrl-C to stop.')
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            print('\nstopped')
