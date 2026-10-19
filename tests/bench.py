"""Latency benchmark against a running bridge: client wall time (incl. process-free HTTP) and server hop/exec split.

Usage: python3 -I tests/bench.py [-n 100] [METHOD:PATH[:BODY] ...]
"""
import argparse, http.client, json, statistics, sys, time

def run(method, path, body, n, port):
    wall, hop, exe = [], [], []
    for _ in range(n):
        c = http.client.HTTPConnection("127.0.0.1", port, timeout=15)
        t = time.perf_counter()
        c.request(method, path, body=body)
        r = c.getresponse(); r.read()
        wall.append((time.perf_counter() - t) * 1000)
        hop.append(int(r.getheader("X-Hop-Us", 0)) / 1000)
        exe.append(int(r.getheader("X-Exec-Us", 0)) / 1000)
        c.close()
        time.sleep(0.01)
    q = lambda xs, p: sorted(xs)[min(len(xs) - 1, int(len(xs) * p))]
    print(f"{method:4} {path:10} n={n}  wall p50={q(wall,.5):6.1f} p99={q(wall,.99):6.1f} max={max(wall):6.1f} ms"
          f" | hop p50={q(hop,.5):5.1f} p99={q(hop,.99):5.1f} | exec p50={q(exe,.5):5.1f} p99={q(exe,.99):5.1f}")

if __name__ == "__main__":
    ap = argparse.ArgumentParser(); ap.add_argument("-n", type=int, default=100); ap.add_argument("--port", type=int, default=47800)
    ap.add_argument("targets", nargs="*", default=["GET:/ping", "GET:/state", "GET:/actions", "GET:/ui"])
    a = ap.parse_args()
    for t in a.targets:
        m, p, *b = t.split(":", 2)
        run(m, p, (b[0] if b else ("{}" if m == "POST" else None)), a.n, a.port)
