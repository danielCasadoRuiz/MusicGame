"""Garment-aware refinement of the skinning weights (runs inside Blender, called by blender_worker.py).

Pipeline position: right after the existing nearest-body-surface transfer (the "seed") and BEFORE the
pose -> rest normalization, for the ULTRA and for every other LOD. Nothing here adds bones, cuts
geometry or touches the export.

1. Topology: the analysis works on a WELDED copy (glTF splits vertices at UV seams; welded by
   position, every garment is one connected surface). Final weights are written identically to all
   duplicates of a welded vertex, so UV seams can never crack.
2. Lower-body structure (legs / skirt panels), measured on the real mesh in the skeleton frame:
   - scanning upwards from the hem, the highest height at which the lower part still splits into two
     large pieces on opposite sides = top of the trouser legs / of the coat's vent ("split height");
   - each piece (with its lining and its thickness walls, which are connected to it) is ONE side;
   - the side labels are grown geodesically over the joined part above; where the two grown sides
     meet is the SEAM (crotch of trousers, junction of the coat panels above the vent).
   - Closed garments (no split) fall back to a lateral split with the seam on the midline.
3. Weights, inside the lower region only (arms/torso untouched):
   - every leg influence is moved to the leg of the vertex's own side (thigh_r -> thigh_l ...);
   - the leg influence is diffused inside each leg/panel (never across the seam) over a radius that
     grows with the cloth-to-skin gap: the legs keep their overall influence (skirts keep following
     the legs) but it is spread over many vertices instead of one row;
   - around the seam the two legs blend smoothly (0.5/0.5 on the seam) and the pelvis takes a share.
4. LODs: the ULTRA result is the reference. Every other LOD finds its own legs/panels and interpolates
   the ULTRA weights from the ULTRA surface OF THE SAME SIDE (a vertex of the left panel can only read
   the left panel), then a light smoothing; topology differences are reported.
5. Validation (optional): temporary test poses on the rest-pose result (always reverted).
"""
from __future__ import annotations
import heapq, math
import numpy as np
import bpy
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

from garment_profiles import profile_for

NONE, LEFT, RIGHT = 0, 1, 2
CHAIN = {LEFT: ("thigh_l", "calf_l", "foot_l", "ball_l"), RIGHT: ("thigh_r", "calf_r", "foot_r", "ball_r")}
INF = float("inf")


# ───────────────────────────── mesh / skeleton helpers ─────────────────────────────

def mesh_world_coords(obj):
    me = obj.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3).astype(np.float64)
    m = np.array(obj.matrix_world, dtype=np.float64)
    return co @ m[:3, :3].T + m[:3, 3]


class Topology:
    """Welded connectivity of a mesh (UV-seam duplicates merged by position)."""

    def __init__(self, obj, eps_rel=1e-5):
        co = mesh_world_coords(obj)
        n = len(co)
        diag = float(np.linalg.norm(co.max(0) - co.min(0))) or 1.0
        eps = diag * eps_rel
        kd = KDTree(n)
        for i in range(n):
            kd.insert(co[i], i)
        kd.balance()
        rep = np.arange(n)
        for i in range(n):
            if rep[i] != i:
                continue
            for _, j, _ in kd.find_range(co[i], eps):
                if j > i and rep[j] == j:
                    rep[j] = i
        roots, wid = np.unique(rep, return_inverse=True)
        self.n, self.m, self.wid = n, len(roots), wid
        self.pos = co[roots]
        me = obj.data
        ev = np.empty(len(me.edges) * 2, dtype=np.int32)
        me.edges.foreach_get("vertices", ev)
        e = wid[ev.reshape(-1, 2)]
        e = e[e[:, 0] != e[:, 1]]
        e.sort(axis=1)
        self.edges = np.unique(e, axis=0)
        self.elen = np.linalg.norm(self.pos[self.edges[:, 0]] - self.pos[self.edges[:, 1]], axis=1)
        me.calc_loop_triangles()
        tv = np.empty(len(me.loop_triangles) * 3, dtype=np.int32)
        me.loop_triangles.foreach_get("vertices", tv)
        t = wid[tv.reshape(-1, 3)]
        self.tris = t[(t[:, 0] != t[:, 1]) & (t[:, 1] != t[:, 2]) & (t[:, 0] != t[:, 2])]
        self.adj = [[] for _ in range(self.m)]
        for (a, b), l in zip(self.edges.tolist(), self.elen.tolist()):
            self.adj[a].append((b, l))
            self.adj[b].append((a, l))


def body_frame(arm):
    """hip origin + lateral (towards the LEFT leg) / up / forward (towards the toes) axes, in world space."""
    aw = arm.matrix_world
    pb = arm.pose.bones
    head = lambda n: aw @ pb[n].head
    hl, hr = head("thigh_l"), head("thigh_r")
    lat = (hl - hr).normalized()
    hip = (hl + hr) * 0.5
    up = head("neck_01") - head("pelvis")
    up = (up - lat * up.dot(lat)).normalized()
    fwd = up.cross(lat).normalized()
    toes = (head("ball_l") - head("foot_l")) + (head("ball_r") - head("foot_r"))
    if fwd.dot(toes) < 0:
        fwd = -fwd
    return {"hip": np.array(hip), "lat": np.array(lat), "up": np.array(up), "fwd": np.array(fwd),
            "v_lat": lat.copy(), "v_up": up.copy(), "v_fwd": fwd.copy()}


def frame_coords(pos, fr):
    d = pos - fr["hip"]
    return d @ fr["lat"], d @ fr["up"], d @ fr["fwd"]


def deform_bones(arm):
    return [b.name for b in arm.data.bones if b.use_deform]


def read_weights(obj, bones):
    idx = {b: k for k, b in enumerate(bones)}
    gi = {vg.index: idx.get(vg.name) for vg in obj.vertex_groups}
    W = np.zeros((len(obj.data.vertices), len(bones)), dtype=np.float64)
    for v in obj.data.vertices:
        for g in v.groups:
            k = gi.get(g.group)
            if k is not None:
                W[v.index, k] = g.weight
    return W


def write_weights(obj, bones, W):
    """Replace the deform vertex groups with W (mesh-vertex rows), exactly like the seed transfer does."""
    deform = set(bones)
    for vg in list(obj.vertex_groups):
        if vg.name in deform:
            obj.vertex_groups.remove(vg)
    groups = [obj.vertex_groups.new(name=b) for b in bones]
    for k, vg in enumerate(groups):
        col = W[:, k]
        for i in np.nonzero(col > 0)[0].tolist():
            vg.add([i], float(col[i]), "REPLACE")


def weld_rows(top, W):
    Ww = np.zeros((top.m, W.shape[1]))
    np.add.at(Ww, top.wid, W)
    cnt = np.bincount(top.wid, minlength=top.m).astype(np.float64)
    return Ww / np.maximum(cnt, 1)[:, None]


def prune_normalize(W, max_inf, min_w):
    W = W.copy()
    if W.shape[1] > max_inf:
        drop = np.argsort(-W, axis=1)[:, max_inf:]
        np.put_along_axis(W, drop, 0.0, axis=1)
    W[W < min_w] = 0.0
    s = W.sum(1, keepdims=True)
    return np.where(s > 1e-12, W / np.maximum(s, 1e-12), W)


def smoothstep(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3 - 2 * x)


# ───────────────────────────── graph algorithms ─────────────────────────────

def dijkstra(top, sources, allowed, labels_of_sources=None):
    """Multi-source geodesic distance over welded edges, restricted to `allowed` vertices.
    sources: list of vertex ids; labels_of_sources: same-length labels to propagate (optional)."""
    dist = [INF] * top.m
    lab = [NONE] * top.m
    heap = []
    for k, v in enumerate(sources):
        dist[v] = 0.0
        if labels_of_sources is not None:
            lab[v] = labels_of_sources[k]
        heap.append((0.0, v))
    heapq.heapify(heap)
    adj = top.adj
    while heap:
        d, v = heapq.heappop(heap)
        if d > dist[v]:
            continue
        lv = lab[v]
        for u, w in adj[v]:
            if not allowed[u]:
                continue
            nd = d + w
            if nd < dist[u]:
                dist[u] = nd
                lab[u] = lv
                heapq.heappush(heap, (nd, u))
    return np.array(dist), np.array(lab, dtype=np.int8)


def find_split(top, s, h, region, min_share, step=0.01):
    """Highest height at which the region below it is split into two large pieces on opposite sides.
    Incremental union-find over vertices sorted by height. Returns (split_h, [vertsA], [vertsB]) or None."""
    order = [int(v) for v in np.argsort(h) if region[v]]
    if len(order) < 50:
        return None
    parent = list(range(top.m))
    size = [1] * top.m
    ssum = [float(x) for x in s]
    active = [False] * top.m
    roots = {}

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    best = None
    count = 0
    next_sample = h[order[0]] + step

    def evaluate(at_h):
        nonlocal best
        if count < 50 or len(roots) < 2:
            return
        (ra, sa), (rb, sb) = heapq.nlargest(2, roots.items(), key=lambda kv: kv[1])
        if sa < min_share * count or sb < min_share * count:
            return
        ma, mb = ssum[ra] / sa, ssum[rb] / sb
        if ma * mb < 0 and min(abs(ma), abs(mb)) > 0.02:
            best = at_h

    for v in order:
        while h[v] > next_sample:
            evaluate(next_sample)
            next_sample += step
        active[v] = True
        roots[v] = 1
        count += 1
        for u, _ in top.adj[v]:
            if not active[u]:
                continue
            a, b = find(v), find(u)
            if a == b:
                continue
            if size[a] < size[b]:
                a, b = b, a
            parent[b] = a
            size[a] += size[b]
            ssum[a] += ssum[b]
            roots[a] = size[a]
            del roots[b]
    evaluate(next_sample)
    if best is None:
        return None

    # Membership of the two pieces at the split height (second, plain pass).
    below = region & (h <= best)
    seen = np.zeros(top.m, dtype=bool)
    comps = []
    for v in np.nonzero(below)[0].tolist():
        if seen[v]:
            continue
        seen[v] = True
        stack, comp = [v], []
        while stack:
            c = stack.pop()
            comp.append(c)
            for u, _ in top.adj[c]:
                if below[u] and not seen[u]:
                    seen[u] = True
                    stack.append(u)
        comps.append(comp)
    comps.sort(key=len, reverse=True)
    return best, comps[0], comps[1]


# ───────────────────────────── lower-body analysis ─────────────────────────────

def analyse_lower(top, s, h, f, region, prof, garment_type):
    """Side labels (LEFT/RIGHT) for the lower region + geodesic distance to the seam + structure report."""
    rep = {}
    split = find_split(top, s, h, region, float(prof["split_min_share"]))
    if split is not None:
        split_h, a, b = split
        side_a = LEFT if s[a].mean() > 0 else RIGHT
        side_b = LEFT if s[b].mean() > 0 else RIGHT
        sources = a + b
        labs = [side_a] * len(a) + [side_b] * len(b)
        _, labels = dijkstra(top, sources, region, labs)
        unreached = region & (labels == NONE)
        labels[unreached] = np.where(s[unreached] > 0, LEFT, RIGHT)
        rep.update(mode="split", split_height_above_hip_m=round(float(split_h), 3),
                   pieces={"L" if side_a == LEFT else "R": len(a), "L" if side_b == LEFT else "R": len(b)},
                   unreached_vertices=int(unreached.sum()))
        if side_a == side_b:
            rep["warning"] = "both large pieces on the same side"
    else:
        labels = np.where(region, np.where(s > 0, LEFT, RIGHT), NONE).astype(np.int8)
        rep.update(mode="closed", note="no separate legs/panels found: lateral split with the seam on the midline")

    e = top.edges
    both = region[e[:, 0]] & region[e[:, 1]]
    seam_e = e[both & (labels[e[:, 0]] != labels[e[:, 1]])]
    seam_v = np.unique(seam_e.ravel()) if len(seam_e) else np.array([], dtype=np.int64)
    if len(seam_v):
        d_seam, _ = dijkstra(top, seam_v.tolist(), region)
        sh, sf = h[seam_v], f[seam_v]
        rep["seam"] = {"vertices": int(len(seam_v)), "height_above_hip_m": [round(float(sh.min()), 3), round(float(sh.max()), 3)],
                       "front": int((sf > 0).sum()), "back": int((sf <= 0).sum())}
        lower_bottom = float(h[region].min())
        # A junction of the two panels far down the skirt (more than halfway from hip to hem) is not a
        # normal vent top: report it as a possible geometric bridge (never cut automatically).
        if rep["mode"] == "split" and garment_type != "trousers" and float(sh.min()) < 0.5 * lower_bottom:
            rep["possible_bridge"] = {"lowest_junction_above_hip_m": round(float(sh.min()), 3),
                                      "hem_above_hip_m": round(lower_bottom, 3)}
    else:
        d_seam = np.full(top.m, INF)
        rep["seam"] = None
    return labels, d_seam, rep


def cloth_gap(body, top, verts):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(dg)
    me = ev.to_mesh()
    try:
        me.calc_loop_triangles()
        mw = ev.matrix_world
        tree = BVHTree.FromPolygons([mw @ v.co for v in me.vertices], [tuple(t.vertices) for t in me.loop_triangles])
    finally:
        ev.to_mesh_clear()
    d = [tree.find_nearest(Vector(top.pos[v]))[3] or 0.0 for v in verts]
    return float(np.median(d)) if d else 0.0


# ───────────────────────────── weight distribution ─────────────────────────────

def smooth_columns(top, X, usable_edges, rows_mask, radius, lam=0.5, max_iter=300):
    """Laplacian diffusion of the columns of X over `usable_edges` (blocked elsewhere), radius in metres."""
    if len(usable_edges) == 0 or radius <= 0:
        return X, 0
    a, b = usable_edges[:, 0], usable_edges[:, 1]
    mean_e = float(np.linalg.norm(top.pos[a] - top.pos[b], axis=1).mean()) or 1e-3
    iters = int(min(max_iter, max(1, round((radius / mean_e) ** 2 / 2))))
    deg = (np.bincount(a, minlength=top.m) + np.bincount(b, minlength=top.m)).astype(np.float64)
    upd = rows_mask & (deg > 0)
    X = X.copy()
    for _ in range(iters):
        nb = np.empty_like(X)
        for c in range(X.shape[1]):
            acc = np.bincount(a, weights=X[b, c], minlength=top.m) + np.bincount(b, weights=X[a, c], minlength=top.m)
            nb[:, c] = acc / np.maximum(deg, 1)
        X[upd] = (1 - lam) * X[upd] + lam * nb[upd]
    return X, iters


def chain_cols(bones):
    bi = {b: k for k, b in enumerate(bones)}
    return [bi[b] for b in CHAIN[LEFT]], [bi[b] for b in CHAIN[RIGHT]], bi.get("pelvis")


def refine_lower(top, W, bones, labels, d_seam, region, prof, gap):
    Lc, Rc, pel = chain_cols(bones)
    W = W.copy()
    lab = labels
    rows = region & (lab != NONE)
    legL, legR = W[:, Lc], W[:, Rc]
    own = np.where(rows[:, None], legL + legR, 0.0)   # chain order thigh/calf/foot/ball, both sides merged into own side

    # 1. spread the leg influence inside each leg / panel (never across the seam)
    e = top.edges
    usable = e[rows[e[:, 0]] & rows[e[:, 1]] & (lab[e[:, 0]] == lab[e[:, 1]])]
    radius = min(float(prof["smooth_radius_m"]) + float(prof["smooth_radius_per_gap"]) * gap, float(prof["smooth_radius_max_m"]))
    own, iters = smooth_columns(top, own, usable, rows, radius)
    own *= float(prof["leg_influence_scale"])
    cap = prof.get("leg_share_cap")
    if cap is not None:
        tot = own.sum(1)
        scale = np.where(tot > cap, cap / np.maximum(tot, 1e-12), 1.0)
        own *= scale[:, None]

    # 2. seam: smooth left/right blend + pelvis share
    band = min(float(prof["seam_band_m"]) + float(prof["seam_band_per_gap"]) * gap, float(prof["seam_band_max_m"]))
    t = smoothstep(np.where(np.isfinite(d_seam), d_seam / max(band, 1e-6), 1.0))
    own_frac = 0.5 + 0.5 * t
    pel_frac = float(prof["seam_pelvis_share"]) * (1.0 - t)
    keep = own * (1.0 - pel_frac)[:, None]
    mine, other = keep * own_frac[:, None], keep * (1.0 - own_frac)[:, None]
    pel_add = own.sum(1) * pel_frac

    # 3. assemble: legs as computed, the rest of the vertex keeps its own (non-leg) proportions
    new = W.copy()
    newL = np.where((lab == LEFT)[:, None], mine, other)
    newR = np.where((lab == RIGHT)[:, None], mine, other)
    leg_cols = Lc + Rc
    nonleg = W.copy()
    nonleg[:, leg_cols] = 0.0
    leg_sum = newL.sum(1) + newR.sum(1)
    target = np.clip(1.0 - leg_sum - pel_add, 0.0, 1.0)
    nl_sum = nonleg.sum(1)
    scaled = nonleg * np.where(nl_sum > 1e-9, target / np.maximum(nl_sum, 1e-9), 0.0)[:, None]
    if pel is not None:
        scaled[:, pel] += np.where(nl_sum > 1e-9, 0.0, target) + pel_add
    new[rows] = scaled[rows]
    new[np.ix_(rows, Lc)] = newL[rows]
    new[np.ix_(rows, Rc)] = newR[rows]
    return new, {"smooth_radius_m": round(radius, 3), "smooth_iterations": iters, "seam_band_m": round(band, 3)}


# ───────────────────────────── ULTRA reference -> LOD ─────────────────────────────

def make_reference(top, W, labels, region, analysis, params):
    return {"pos": top.pos.copy(), "tris": top.tris.copy(), "W": W.copy(), "labels": labels.copy(),
            "region": region.copy(), "analysis": analysis, "params": params}


def _barycentric(p, a, b, c):
    v0, v1, v2 = b - a, c - a, p - a
    d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
    d20, d21 = v2.dot(v0), v2.dot(v1)
    den = d00 * d11 - d01 * d01
    if abs(den) < 1e-20:
        return 1.0, 0.0, 0.0
    v = (d11 * d20 - d01 * d21) / den
    w = (d00 * d21 - d01 * d20) / den
    return 1.0 - v - w, v, w


def transfer_from_reference(ref, top, labels, region):
    pos_v = [Vector(p) for p in ref["pos"]]
    tris = ref["tris"]
    rl = ref["labels"]
    trees = {}
    for key, sel in (("all", np.ones(len(tris), bool)),
                     (LEFT, (rl[tris] == LEFT).all(1)), (RIGHT, (rl[tris] == RIGHT).all(1))):
        idx = np.nonzero(sel)[0]
        if len(idx):
            trees[key] = (BVHTree.FromPolygons(pos_v, tris[idx].tolist()), idx)
    W = np.zeros((top.m, ref["W"].shape[1]))
    disagree = 0
    for v in range(top.m):
        p = Vector(top.pos[v])
        key = labels[v] if (region[v] and labels[v] in trees) else "all"
        tree, idx = trees[key]
        loc, _, ti, _ = tree.find_nearest(p)
        if ti is None:
            tree, idx = trees["all"]
            loc, _, ti, _ = tree.find_nearest(p)
        ia, ib, ic = tris[idx[ti]]
        u, w1, w2 = _barycentric(loc, pos_v[ia], pos_v[ib], pos_v[ic])
        W[v] = u * ref["W"][ia] + w1 * ref["W"][ib] + w2 * ref["W"][ic]
        if region[v] and labels[v] != NONE and key == "all":
            disagree += 1
    return np.clip(W, 0.0, None), disagree


# ───────────────────────────── when to act (garments that already work keep their weights) ─────────────────────────────

def seed_problems(top, W, bones, labels, region, d_seam, seam_tolerance=0.03):
    """Problems of the seed weights inside the lower region, per welded vertex:
    - an edge whose ends follow opposite legs (each > 0.6)          -> tearing between the legs
    - a vertex dominated by the leg of the OTHER side (side from connectivity; a small share is the
      body's own crotch blend, and within `seam_tolerance` of the seam mixed legs are expected) -> wrong leg
    - an edge where the dominant leg bone switches abruptly (thigh -> calf ...) by > 0.8
    Returns (mask of problem vertices, counts)."""
    Lc, Rc, _ = chain_cols(bones)
    wl, wr = W[:, Lc].sum(1), W[:, Rc].sum(1)
    e = top.edges
    a, b = e[:, 0], e[:, 1]
    inreg = region[a] & region[b]
    lr = inreg & (((wl[a] > 0.6) & (wr[b] > 0.6)) | ((wr[a] > 0.6) & (wl[b] > 0.6)))
    own = np.where((labels == LEFT)[:, None], W[:, Lc], W[:, Rc])          # chain of the vertex's own side
    other = np.where(labels == LEFT, wr, np.where(labels == RIGHT, wl, 0.0))
    cross = region & (labels != NONE) & (other > np.maximum(0.3, own.sum(1))) & ~(d_seam < seam_tolerance)
    lt = np.maximum(own.sum(1), 1e-9)
    share = own / lt[:, None]
    abrupt = inreg & (np.abs(share[a] - share[b]).max(1) > 0.8) & (np.minimum(own.sum(1)[a], own.sum(1)[b]) > 0.3)
    mask = cross.copy()
    for sel in (lr, abrupt):
        mask[a[sel]] = True
        mask[b[sel]] = True
    return mask, {"edges_opposite_legs": int(lr.sum()), "vertices_other_side_leg": int(cross.sum()),
                  "edges_abrupt_leg_bone_switch": int(abrupt.sum())}


def blend_near(top, W_seed, W_new, problem, region, radius):
    """Keep the refined weights within `radius` (geodesic) of the problems, the seed beyond 2*radius."""
    d, _ = dijkstra(top, np.nonzero(problem)[0].tolist(), region)
    alpha = 1.0 - smoothstep(np.where(np.isfinite(d), (d - radius) / max(radius, 1e-6), 1.0))
    return alpha[:, None] * W_new + (1.0 - alpha[:, None]) * W_seed, int((alpha > 0.01).sum())


# ───────────────────────────── entry point ─────────────────────────────

def refine(garment, arm, body, cfg_section, garment_type, overrides, max_inf, min_w,
           reference=None, build_reference=False, log=print):
    """Refines the seed weights already on `garment` (fit pose). Returns (report, reference_or_None)."""
    prof = profile_for(garment_type, cfg_section, overrides)
    bones = deform_bones(arm)
    top = Topology(garment, float((cfg_section or {}).get("weld_epsilon_rel", 1e-5)))
    W_mesh = read_weights(garment, bones)
    Ww = weld_rows(top, W_mesh)
    fr = body_frame(arm)
    s, h, f = frame_coords(top.pos, fr)
    Lc, Rc, _ = chain_cols(bones)
    leg_tot = Ww[:, Lc].sum(1) + Ww[:, Rc].sum(1)
    region = (h <= float(prof["region_top_above_hip_m"])) | (leg_tot > 0.01)
    report = {"garment_type": garment_type, "mesh_vertices": top.n, "welded_vertices": top.m,
              "uv_split_duplicates": top.n - top.m, "region_vertices": int(region.sum())}

    if not prof["lower_refine"] or region.sum() == 0:
        report["lower"] = "skipped"
        return report, None

    labels, d_seam, analysis = analyse_lower(top, s, h, f, region, prof, garment_type)
    report["structure"] = analysis

    if reference is not None and reference["params"].get("mode") == "keep":
        report["params"] = dict(reference["params"])
        log(f"GARMENT WEIGHTS {garment_type}: keep (as the ULTRA) - seed weights unchanged")
        return report, None
    if reference is not None:
        W_new, disagree = transfer_from_reference(reference, top, labels, region)
        rows = region & (labels != NONE)
        e = top.edges
        usable = e[rows[e[:, 0]] & rows[e[:, 1]] & (labels[e[:, 0]] == labels[e[:, 1]])]
        used = np.nonzero(W_new[rows].sum(0) > 0)[0] if rows.any() else np.array([], int)
        if len(used):
            sm, iters = smooth_columns(top, W_new[:, used], usable, rows, float(prof["lod_post_smooth_m"]))
            W_new[:, used] = sm
        ref_an = reference["analysis"]
        report["from_ultra"] = {"labels_without_same_side_reference": int(disagree),
                                "ultra_mode": ref_an.get("mode"), "lod_mode": analysis.get("mode"),
                                "ultra_split_height_m": ref_an.get("split_height_above_hip_m"),
                                "lod_split_height_m": analysis.get("split_height_above_hip_m")}
        if ref_an.get("mode") != analysis.get("mode") or (
                ref_an.get("split_height_above_hip_m") is not None and analysis.get("split_height_above_hip_m") is not None
                and abs(ref_an["split_height_above_hip_m"] - analysis["split_height_above_hip_m"]) > 0.05):
            report["from_ultra"]["topology_warning"] = "this LOD's legs/panels differ from the ULTRA (retopology): check the deformation report"
        params = reference["params"]
    else:
        gap = cloth_gap(body, top, np.nonzero(region)[0].tolist())
        problem, counts = seed_problems(top, Ww, bones, labels, region, d_seam)
        report["seed_problems"] = counts
        loose = gap >= float(prof["loose_gap_m"])
        if not problem.any():
            mode = "keep"           # nothing wrong: the transferred body weights stay exactly as they are
            W_new, params = Ww, {}
        else:
            W_new, params = refine_lower(top, Ww, bones, labels, d_seam, region, prof, gap)
            mode = "full" if loose else "local"
            if mode == "local":     # close-fitting garment: only repair around the problems
                W_new, changed = blend_near(top, Ww, W_new, problem, region, float(prof["local_radius_m"]))
                params["local_changed_vertices"] = changed
        params.update(mode=mode, cloth_gap_median_m=round(gap, 3))
        if mode == "keep":
            report["params"] = params
            log(f"GARMENT WEIGHTS {garment_type}: keep (no problems found, gap={gap:.3f} m) - seed weights unchanged")
            ref = make_reference(top, Ww, labels, region, analysis, params) if build_reference else None
            return report, ref
    report["params"] = params

    W_new = prune_normalize(W_new, max_inf, min_w)
    write_weights(garment, bones, W_new[top.wid])
    ref = make_reference(top, W_new, labels, region, analysis, params) if build_reference else None
    log(f"GARMENT WEIGHTS {garment_type}: {analysis.get('mode')} split={analysis.get('split_height_above_hip_m')} "
        f"seam={analysis.get('seam')} params={params}" + (f" from_ultra={report.get('from_ultra')}" if reference is not None else ""))
    return report, ref


# ───────────────────────────── validation (test poses, always reverted) ─────────────────────────────

def _rotate(arm, bone, axis_world, deg):
    pb = arm.pose.bones[bone]
    ax = (arm.matrix_world.inverted().to_3x3() @ axis_world).normalized()
    pivot = pb.head.copy()
    pb.matrix = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(deg), 4, ax) @ Matrix.Translation(-pivot) @ pb.matrix
    bpy.context.view_layer.update()


def _rotate_towards(arm, bone, axis, probe, direction, deg):
    p0 = arm.matrix_world @ arm.pose.bones[probe].head
    _rotate(arm, bone, axis, 5)
    p1 = arm.matrix_world @ arm.pose.bones[probe].head
    _rotate(arm, bone, axis, -5)
    _rotate(arm, bone, axis, deg if (p1 - p0).dot(direction) > 0 else -deg)


def _reset(arm):
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def test_poses(fr):
    lat, fwd = fr["v_lat"], fr["v_fwd"]
    out_l, out_r = lat, -lat
    return {
        "spread_45": [("thigh_l", fwd, "calf_l", out_l, 22.5), ("thigh_r", fwd, "calf_r", out_r, 22.5)],
        "spread_90": [("thigh_l", fwd, "calf_l", out_l, 45), ("thigh_r", fwd, "calf_r", out_r, 45)],
        "front_kick_L": [("thigh_l", lat, "calf_l", fwd, 90)],
        "side_kick_L": [("thigh_l", fwd, "calf_l", out_l, 80)],
        "knee_flex_L": [("calf_l", lat, "foot_l", -fwd, 100)],
        "run_stride": [("thigh_l", lat, "calf_l", fwd, 40), ("calf_l", lat, "foot_l", -fwd, 30),
                       ("thigh_r", lat, "calf_r", -fwd, 25), ("calf_r", lat, "foot_r", -fwd, 80)],
        "run_stride_R": [("thigh_r", lat, "calf_r", fwd, 40), ("calf_r", lat, "foot_r", -fwd, 30),
                         ("thigh_l", lat, "calf_l", -fwd, 25), ("calf_l", lat, "foot_l", -fwd, 80)],
    }


def _evaluated_coords(obj):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    me = ev.to_mesh()
    try:
        co = np.empty(len(me.vertices) * 3, dtype=np.float32)
        me.vertices.foreach_get("co", co)
    finally:
        ev.to_mesh_clear()
    return co.reshape(-1, 3).astype(np.float64)


def _body_tree(body):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(dg)
    me = ev.to_mesh()
    try:
        me.calc_loop_triangles()
        mw = ev.matrix_world
        return BVHTree.FromPolygons([mw @ v.co for v in me.vertices], [tuple(t.vertices) for t in me.loop_triangles])
    finally:
        ev.to_mesh_clear()


def _inside_body(tree, pts, tol=0.003):
    """Garment points that ended up under the skin (nearest body surface faces away from them)."""
    n = 0
    for p in pts:
        loc, nor, _, _ = tree.find_nearest(Vector(p))
        if loc is not None and (Vector(p) - loc).dot(nor) < -tol:
            n += 1
    return n


def deformation_report(garment, arm, body=None):
    """Rest-pose garment + armature. Static weight checks and edge stretch / displacement per test pose
    (+ garment vertices pushed under the skin when the body is given)."""
    _reset(arm)
    bones = deform_bones(arm)
    Lc, Rc, _ = chain_cols(bones)
    W = read_weights(garment, bones)
    wl, wr = W[:, Lc].sum(1), W[:, Rc].sum(1)
    fr = body_frame(arm)
    world = mesh_world_coords(garment)
    s, h, _ = frame_coords(world, fr)
    me = garment.data
    ev = np.empty(len(me.edges) * 2, dtype=np.int32)
    me.edges.foreach_get("vertices", ev)
    E = ev.reshape(-1, 2)
    lr = ((wl[E[:, 0]] > 0.6) & (wr[E[:, 1]] > 0.6)) | ((wr[E[:, 0]] > 0.6) & (wl[E[:, 1]] > 0.6))
    rep = {"static": {
        "edges_direct_L_to_R_leg_jump": int(lr.sum()),
        "leg_vertices_on_wrong_side_>3cm": int(((wl > 0.5) & (s < -0.03)).sum() + ((wr > 0.5) & (s > 0.03)).sum()),
        "vertices_with_leg_influence": int(((wl + wr) > 0.05).sum()),
        "mean_leg_influence_below_hip": round(float((wl + wr)[h < 0].mean()) if (h < 0).any() else 0.0, 3),
        "thickness_side_consistency": _thickness_consistency(garment, world, wl, wr, h),
    }}
    rest_co = _evaluated_coords(garment)
    rest_len = np.linalg.norm(rest_co[E[:, 0]] - rest_co[E[:, 1]], axis=1)
    lower = h < 0
    hem = h < (h.min() + 0.15 * (h.max() - h.min()))
    near_body_idx = np.nonzero(h < 0.15)[0]
    mw = np.array(garment.matrix_world)
    poses = {}
    try:
        for name, ops in test_poses(fr).items():
            _reset(arm)
            for bone, axis, probe, direction, deg in ops:
                _rotate_towards(arm, bone, axis, probe, direction, deg)
            co = _evaluated_coords(garment)
            ln = np.linalg.norm(co[E[:, 0]] - co[E[:, 1]], axis=1)
            ratio = np.where(rest_len > 1e-7, ln / np.maximum(rest_len, 1e-7), 1.0)
            disp = np.linalg.norm(co - rest_co, axis=1)
            total = float(rest_len.sum()) or 1.0
            poses[name] = {"p99": round(float(np.percentile(ratio, 99)), 3), "p99.9": round(float(np.percentile(ratio, 99.9)), 2),
                           "max": round(float(ratio.max()), 2),
                           # length-weighted: share of the garment's edge length stretched beyond 1.5x / 2x / 3x
                           "len_share_>1.5x": round(float(rest_len[ratio > 1.5].sum() / total), 4),
                           "len_share_>2x": round(float(rest_len[ratio > 2].sum() / total), 4),
                           "len_share_>3x": round(float(rest_len[ratio > 3].sum() / total), 4),
                           "stretch_energy": round(float((rest_len * np.clip(ratio - 1, 0, None) ** 2).sum() / total), 4),
                           "mean_disp_below_hip_cm": round(float(disp[lower].mean()) * 100, 2) if lower.any() else 0.0,
                           "mean_disp_hem_cm": round(float(disp[hem].mean()) * 100, 2)}
            if body is not None:
                wpts = co[near_body_idx] @ mw[:3, :3].T + mw[:3, 3]
                poses[name]["vertices_inside_body"] = _inside_body(_body_tree(body), wpts)
    finally:
        _reset(arm)
    rep["poses"] = poses
    return rep


def _thickness_consistency(garment, world, wl, wr, h, samples=4000, depth=0.04):
    """Share of lower faces whose surface right behind them (lining / other side of the thickness) follows
    the same leg. Only faces with a clear leg side on both surfaces count."""
    me = garment.data
    polys = [tuple(p.vertices) for p in me.polygons]
    tree = BVHTree.FromPolygons([Vector(p) for p in world], polys)
    m3 = garment.matrix_world.to_3x3()
    side = wl - wr
    idx = [i for i, p in enumerate(me.polygons) if h[list(p.vertices)].mean() < 0]
    if not idx:
        return None
    step = max(1, len(idx) // samples)
    same = tot = 0
    for i in idx[::step]:
        p = me.polygons[i]
        vs = list(p.vertices)
        sa = side[vs].mean()
        if abs(sa) < 0.3:
            continue
        c = Vector(world[vs].mean(0))
        n = (m3 @ p.normal).normalized()
        _, _, j, _ = tree.ray_cast(c - n * 1e-4, -n, depth)
        if j is None or j == i:
            continue
        sb = side[list(me.polygons[j].vertices)].mean()
        if abs(sb) < 0.3:
            continue
        tot += 1
        same += (sa > 0) == (sb > 0)
    return round(same / tot, 4) if tot else None
