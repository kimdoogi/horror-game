"""Measures anatomical landmarks on a standing humanoid mesh, for rig fitting.

The mesh arrives Z-up, facing -Y, feet on Z=0. Everything works off horizontal slabs
of vertices, and within a slab off **connected components in the XY plane**. That is
the reading that survives an arbitrary sculpt: two components low down are two feet,
they stay two all the way up the legs and merge at the crotch; a component outboard
of the torso is an arm; the slab where the single upper component is narrowest is the
neck. No template fitting, no assumption about how the artist built the mesh.

An earlier version looked for a gap in the X values straddling the centreline. It
read the crotch at chest height, because a horizontal slice of a solid torso is a
closed loop whose vertices near X=0 are sparse — the sampling gap between two
neighbouring surface vertices looked exactly like air between two legs.
"""
from __future__ import annotations

from mathutils import Vector


def _components(points, tol):
    """Connected components of 2D points, linked when within `tol` of each other.

    Grid-bucketed union-find: each point drops into a cell of side `tol`, and only
    the 8 neighbouring cells can hold anything close enough to join it. Linear in
    the number of points, which matters because this runs on every slab.
    """
    cells: dict[tuple[int, int], list[int]] = {}
    for i, p in enumerate(points):
        cells.setdefault((int(p[0] // tol), int(p[1] // tol)), []).append(i)

    parent = list(range(len(points)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[rb] = ra

    t2 = tol * tol
    for (cx, cy), members in cells.items():
        for dx in (0, 1):
            for dy in (-1, 0, 1):
                if dx == 0 and dy < 0:
                    continue
                other = cells.get((cx + dx, cy + dy))
                if not other:
                    continue
                for i in members:
                    pi = points[i]
                    for j in other:
                        if i == j:
                            continue
                        pj = points[j]
                        if (pi[0] - pj[0]) ** 2 + (pi[1] - pj[1]) ** 2 <= t2:
                            union(i, j)

    groups: dict[int, list[int]] = {}
    for i in range(len(points)):
        groups.setdefault(find(i), []).append(i)

    out = []
    for members in groups.values():
        xs = [points[i][0] for i in members]
        ys = [points[i][1] for i in members]
        out.append({
            "n": len(members),
            "x0": min(xs), "x1": max(xs),
            "y0": min(ys), "y1": max(ys),
            "cx": sum(xs) / len(xs), "cy": sum(ys) / len(ys),
        })
    out.sort(key=lambda c: c["cx"])
    return out


def measure(obj, slabs=200, verbose=False):
    """Returns a dict of landmark heights and widths, all in metres."""
    verts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    lo = Vector((min(c.x for c in verts), min(c.y for c in verts), min(c.z for c in verts)))
    hi = Vector((max(c.x for c in verts), max(c.y for c in verts), max(c.z for c in verts)))
    height = hi.z - lo.z
    tol = height * 0.022

    step = height / slabs
    buckets = [[] for _ in range(slabs)]
    for co in verts:
        i = min(slabs - 1, max(0, int((co.z - lo.z) / step)))
        buckets[i].append((co.x, co.y))

    def z_of(i):
        return lo.z + (i + 0.5) * step

    # Every "is this mass real or is it noise" threshold scales with how densely the
    # mesh is sampled. Fixed counts silently change meaning: tuned on a 23k-vertex
    # sculpt, `n >= 12` means "a limb" — run the same numbers over that sculpt after
    # decimation to 5.6k and it means "a limb, most of the time", which moved the
    # vessel's ankle from 0.088 m to 0.029 m and put its foot bone through the floor.
    density = len(verts) / float(slabs)
    n_small = max(3, int(round(density * 0.035)))
    n_solid = max(3, int(round(density * 0.100)))

    comps = [_components(b, tol) if len(b) >= n_small else [] for b in buckets]

    # ── legs and the crotch ─────────────────────────────────────────────────
    # The crotch is the lowest slab where a single mass straddles the centreline.
    # Below it the legs are two masses with air between them and nothing crosses
    # X=0; at it and above it the pelvis is continuous. Counting masses instead —
    # "walk up while there are still two" — reads far too high, because the arms
    # hang beside the torso and are themselves two extra masses all the way down.
    #
    # Confirmed over four consecutive slabs so a pair of touching thighs cannot
    # claim it: they graze and part again, a pelvis does not.
    crotch_i = int(slabs * 0.45)
    for i in range(int(slabs * 0.06), int(slabs * 0.80)):
        run = 0
        for j in range(i, min(i + 4, slabs)):
            big = [c for c in comps[j] if c["n"] >= n_small]
            if any(c["x0"] <= 0.0 <= c["x1"] for c in big):
                run += 1
            else:
                break
        if run >= 4:
            crotch_i = i
            break
    crotch = z_of(crotch_i)

    # ── neck ────────────────────────────────────────────────────────────────
    # Narrowest single mass in the top quarter. The skull flares again above it.
    neck_i, neck_w = None, 1e9
    for i in range(int(slabs * 0.78), int(slabs * 0.97)):
        big = [c for c in comps[i] if c["n"] >= n_small]
        if len(big) != 1:
            continue
        w = big[0]["x1"] - big[0]["x0"]
        if w < neck_w:
            neck_w, neck_i = w, i
    if neck_i is None:
        neck_i = int(slabs * 0.88)
    neck = z_of(neck_i)

    # ── torso, shoulders, arms ──────────────────────────────────────────────
    # In any slab the torso is the mass containing the centreline; anything outboard
    # of it on the +X or -X side is that side's arm.
    def torso_of(i):
        big = [c for c in comps[i] if c["n"] >= n_small]
        inside = [c for c in big if c["x0"] <= 0.0 <= c["x1"]]
        if inside:
            return max(inside, key=lambda c: c["n"])
        return max(big, key=lambda c: c["n"]) if big else None

    # The +X arm is simply the most outboard mass on that side, provided something
    # else on the same side sits inboard of it to be the torso or the leg. Defining
    # it against the torso breaks below the crotch, where there is no torso: the
    # fallback picked a foot as the "torso" and then the other foot as the "arm".
    def arm_of(i, side=+1):
        big = [c for c in comps[i] if c["n"] >= n_small and (c["cx"] > 0.0 if side > 0
                                                       else c["cx"] < 0.0)]
        if len(big) < 2:
            return None
        outer = max(big, key=lambda c: c["cx"] * side)
        inner = min(big, key=lambda c: c["cx"] * side)
        if abs(outer["cx"] - inner["cx"]) < tol:
            return None
        return outer

    # Shoulder: descending from the neck, the first slab whose torso has widened to
    # 70% of the widest torso in the chest band — the deltoid line.
    chest_lo, chest_hi = int(slabs * 0.60), neck_i
    widest = 0.0
    for i in range(chest_lo, chest_hi):
        t = torso_of(i)
        if t:
            widest = max(widest, t["x1"] - t["x0"])
    sh_i = neck_i
    for i in range(neck_i - 1, chest_lo, -1):
        t = torso_of(i)
        if t and (t["x1"] - t["x0"]) >= widest * 0.70:
            sh_i = i
            break
    shoulder = z_of(sh_i)
    t_sh = torso_of(sh_i)
    shoulder_width = (t_sh["x1"] - t_sh["x0"]) if t_sh else height * 0.30

    # The arm chain: walk down from the shoulder while an outboard mass keeps
    # appearing, tolerating two blank slabs so a thin wrist cannot cut the chain.
    # Walking rather than collecting matters — anything found below the hand belongs
    # to the feet, and a plain `min()` over all hits would put the fingertip there.
    # Two conditions, and both are needed. The miss tolerance is generous because on
    # a figure whose arms hang against its thighs the hand and the leg merge into one
    # mass for a stretch, and a short tolerance ends the chain there and calls the
    # merge point the fingertip. But a generous tolerance alone lets the chain hop
    # clean over the hand and carry on down the *leg*, so each new link must also sit
    # close to the last one in X: an arm tapers, it does not jump inboard.
    arm = {}
    misses = 0
    last_cx = None
    for i in range(sh_i, -1, -1):
        a = arm_of(i, +1)
        if a and last_cx is not None and last_cx - a["cx"] > tol * 2.5:
            a = None                      # that is the leg, not the hand
        if a:
            arm[i] = a
            last_cx = a["cx"]
            misses = 0
        elif arm:
            misses += 1
            if misses > 6:
                break
    fingertip_i = min(arm) if arm else int(slabs * 0.25)
    fingertip = z_of(fingertip_i)

    # Wrist: the pinch immediately above the hand. Found in two steps because the
    # narrowest slab of the whole lower arm is a finger, not the wrist — first locate
    # the hand as the *widest* slab down at that end (the knuckles), then take the
    # narrowest slab above it.
    wrist_i = fingertip_i
    if len(arm) > 8:
        run = sorted(arm)
        widths = {i: arm[i]["x1"] - arm[i]["x0"] for i in run}
        lower = run[: max(3, int(len(run) * 0.45))]
        hand_i = max(lower, key=lambda i: widths[i])
        above = [i for i in run if i > hand_i]
        if above:
            wrist_i = min(above[: max(3, len(above) // 3)], key=lambda i: widths[i])
    wrist = z_of(wrist_i)

    # Elbow: narrowest arm slab between the wrist and the shoulder, biased to the
    # middle so a lumpy forearm cannot claim it.
    elbow_i = (wrist_i + sh_i) // 2
    if len(arm) > 12:
        mid = [i for i in arm if wrist_i + 3 < i < sh_i - 3]
        if mid:
            elbow_i = min(mid, key=lambda i: abs(i - (wrist_i + sh_i) // 2))
    elbow = z_of(elbow_i)

    # ── ankle ───────────────────────────────────────────────────────────────
    # Narrowest leg mass in the bottom quarter, above the foot's flare. The leg is
    # picked as the *inboard* mass on the +X side, never the largest: the hands hang
    # outboard of the shins and are the larger mass in some of these slabs.
    def leg_at(i):
        big = [c for c in comps[i] if c["n"] >= n_small and c["cx"] > 0.0]
        return min(big, key=lambda c: c["cx"]) if big else None

    # A foot is long in Y and narrow in X; a shin is roughly round. So the ankle is
    # the lowest slab where that elongation has gone. Hunting for the narrowest slab
    # instead finds a toe on a clawed foot, which is why this is a shape test and
    # not a size one.
    # The ankle is where the foot stops reaching forward. Measured on the leg's
    # frontmost Y per slab: down at the toe that value is far forward, up the shin it
    # settles back, and the ankle is where it has covered three quarters of the way
    # between the two. Slabs holding fewer than 12 vertices are ignored throughout —
    # they are single toes and stray shards, and their readings are noise.
    #
    # Size-based tests do not work here and the dump says why. The leg's narrowest
    # cross-section, by either width or area, is **mid-shin**: at true ankle height
    # the slab still spans heel to instep, so it measures longer than the calf above
    # it. There is no pinch at an ankle to find on a standing figure.
    def leg_solid(i):
        c = leg_at(i)
        return c if (c is not None and c["n"] >= n_solid) else None

    toe_front = min((c["y0"] for c in (leg_solid(i) for i in range(0, int(slabs * 0.05)))
                     if c), default=lo.y)
    shin_fronts = [c["y0"] for c in (leg_solid(i)
                                     for i in range(int(slabs * 0.12), int(slabs * 0.21)))
                   if c]
    shin_front = (sorted(shin_fronts)[len(shin_fronts) // 2] if shin_fronts
                  else toe_front + height * 0.09)

    ank_i = int(slabs * 0.05)
    for i in range(1, int(slabs * 0.30)):
        c = leg_solid(i)
        if c is None:
            continue
        if c["y0"] >= toe_front + 0.75 * (shin_front - toe_front):
            ank_i = i
            break
    ankle = z_of(ank_i)

    # Knee: midway between ankle and crotch. A knee is not a pinch in the silhouette
    # of a straight leg, so there is nothing to detect — the midpoint is both the
    # honest estimate and close enough, since auto-weights blend across it anyway.
    knee_i = (ank_i + crotch_i) // 2
    knee = z_of(knee_i)

    def leg_x_at(i):
        c = leg_at(i)
        return c["cx"] if c else height * 0.08

    a_sh = arm_of(sh_i, +1)
    foot = [c for c in verts if c.z < lo.z + height * 0.05]

    # Depth references. The crest is grown off the back and the jaw swings out of the
    # front, so both need to know where the torso's surface actually is rather than
    # assuming the sculpt is centred on Y=0 — neither of these two is.
    torso_band = [c for c in verts if crotch < c.z < shoulder and abs(c.x) < height * 0.10]
    head_band = [c for c in verts if c.z > neck]
    back_y = max((c.y for c in torso_band), default=height * 0.06)
    front_y = min((c.y for c in torso_band), default=-height * 0.06)
    head_y = (sum(c.y for c in head_band) / len(head_band)) if head_band else 0.0

    m = {
        "height": height, "floor": lo.z, "top": hi.z,
        "crotch": crotch, "neck": neck, "shoulder": shoulder,
        "shoulder_width": shoulder_width,
        "elbow": elbow, "wrist": wrist, "fingertip": fingertip,
        "knee": knee, "ankle": ankle,
        "leg_x_hip": leg_x_at(max(0, crotch_i - 2)),
        "leg_x_knee": leg_x_at(knee_i),
        "leg_x_ankle": leg_x_at(ank_i),
        "arm_x_shoulder": a_sh["cx"] if a_sh else height * 0.14,
        "arm_x_elbow": arm[elbow_i]["cx"] if elbow_i in arm else height * 0.16,
        "arm_x_wrist": arm[wrist_i]["cx"] if wrist_i in arm else height * 0.17,
        "arm_y_shoulder": a_sh["cy"] if a_sh else 0.0,
        "arm_y_wrist": arm[wrist_i]["cy"] if wrist_i in arm else 0.0,
        "toe_y": min((c.y for c in foot), default=-0.2),
        "heel_y": max((c.y for c in foot), default=0.1),
        "back_y": back_y, "front_y": front_y, "head_y": head_y,
        "bbox_lo": tuple(lo), "bbox_hi": tuple(hi),
    }
    if verbose:
        for i in range(0, slabs, 8):
            big = [c for c in comps[i] if c["n"] >= n_small]
            print(f"  slab {i:3d} z={z_of(i):+.3f} comps={len(big)} "
                  f"xs={[round(c['cx'], 3) for c in big]}")
    return m


def report(name, m):
    print(f"LANDMARKS {name}")
    for k in ("height", "top", "neck", "shoulder", "shoulder_width", "crotch",
              "elbow", "wrist", "fingertip", "knee", "ankle",
              "leg_x_hip", "leg_x_knee", "leg_x_ankle",
              "arm_x_shoulder", "arm_x_elbow", "arm_x_wrist", "toe_y", "heel_y"):
        print(f"  {k:16s} {m[k]:+.4f}")
