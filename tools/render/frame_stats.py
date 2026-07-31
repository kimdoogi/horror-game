"""Objective read on a shot: how much of the frame is legible, how much is crushed
to black, how much is blown to white, and how much colour separation there is.

Judging "is it too dark" by eye across iterations is unreliable — the eye adapts
to whatever it saw last. These numbers do not."""

import sys
import glob
import os
from PIL import Image


def stats(path):
    im = Image.open(path).convert("RGB")
    px = list(im.getdata())
    n = len(px)

    lum = sorted(0.2126 * r + 0.7152 * g + 0.0722 * b for r, g, b in px)

    def pct(p):
        return lum[min(n - 1, int(n * p))]

    # "Crushed" = below 2/255: no recoverable detail on any display.
    crushed = sum(1 for v in lum if v < 2.0) / n
    # "Legible" = 8..235: something a player could actually read shape from.
    legible = sum(1 for v in lum if 8.0 <= v <= 235.0) / n
    blown = sum(1 for v in lum if v > 250.0) / n

    sat = sum((max(p) - min(p)) for p in px) / n
    mean = sum(lum) / n

    return dict(
        name=os.path.basename(path),
        mean=mean,
        p50=pct(0.50),
        p90=pct(0.90),
        p99=pct(0.99),
        crushed=crushed * 100,
        legible=legible * 100,
        blown=blown * 100,
        sat=sat,
    )


def main():
    paths = []
    for pattern in sys.argv[1:]:
        paths.extend(sorted(glob.glob(pattern)))

    print(f"{'shot':38} {'mean':>6} {'p50':>6} {'p90':>6} {'p99':>6} "
          f"{'black%':>7} {'legible%':>9} {'blown%':>7} {'sat':>6}")
    for p in paths:
        s = stats(p)
        print(f"{s['name']:38} {s['mean']:6.1f} {s['p50']:6.1f} {s['p90']:6.1f} "
              f"{s['p99']:6.1f} {s['crushed']:7.1f} {s['legible']:9.1f} "
              f"{s['blown']:7.2f} {s['sat']:6.1f}")


if __name__ == "__main__":
    main()
