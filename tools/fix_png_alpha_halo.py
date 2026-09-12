#!/usr/bin/env python3
"""
Corregeix el halo/fil blanc que apareix en textures PNG amb transparencia
quan Unity les mostra amb filtratge Bilinear/Trilinear des de StreamingAssets.

El problema: als pixels amb alpha = 0 sovint hi queda un RGB blanc o residual
(del software d'exportacio). El filtratge bilinear interpola RGB i alpha per
separat, aixi que aquest RGB "brut" es filtra cap als pixels visibles del
voral i es veu com un halo blanc.

La correccio (equivalent a "Alpha Is Transparency" d'Unity): per a cada
pixel amb alpha = 0, es substitueix el seu RGB pel RGB del pixel visible
(alpha > 0) mes proper, mitjancant una transformada de distancia (nearest
neighbour). No es toca l'alpha, ni els pixels amb alpha > 0 (visibles o
semitransparents), ni les dimensions, ni el nom/ruta del fitxer.

Us:
    python fix_png_alpha_halo.py <carpeta>

Requereix:
    pip install pillow numpy scipy
"""

import sys
import os
import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt


def fix_alpha_halo(image_path):
    """Corregeix el RGB dels pixels amb alpha = 0 d'un PNG, en el mateix fitxer.

    Retorna True si el fitxer s'ha modificat, False si no calia tocar-lo
    (sense canal alpha o sense pixels amb alpha = 0).
    """
    with Image.open(image_path) as img:
        original_mode = img.mode

        if "A" not in img.mode and img.mode != "P":
            return False  # no hi ha canal alpha: no hi ha res a corregir

        rgba = img.convert("RGBA")
        arr = np.array(rgba)

    alpha = arr[:, :, 3]
    transparent_mask = alpha == 0

    if not transparent_mask.any():
        return False  # cap pixel completament transparent

    valid_mask = ~transparent_mask
    if not valid_mask.any():
        return False  # imatge totalment transparent: no hi ha color de referencia

    # Per a cada pixel transparent, index del pixel valid (alpha > 0) mes proper
    _, (rows, cols) = distance_transform_edt(~valid_mask, return_indices=True)

    out = arr.copy()
    nearest_rgb = arr[:, :, :3][rows, cols]
    out[:, :, :3][transparent_mask] = nearest_rgb[transparent_mask]

    # L'alpha es queda exactament igual (no premultiplied, no barreja de fons)
    out[:, :, 3] = alpha

    result = Image.fromarray(out, mode="RGBA")
    result.save(image_path, format="PNG", optimize=False)
    return True


def main():
    if len(sys.argv) != 2:
        print("Us: python fix_png_alpha_halo.py <carpeta>")
        sys.exit(1)

    root = sys.argv[1]
    if not os.path.isdir(root):
        print(f"Error: '{root}' no es una carpeta valida")
        sys.exit(1)

    total = 0
    fixed = 0
    skipped = 0
    errors = 0

    for dirpath, _, filenames in os.walk(root):
        for fname in filenames:
            if not fname.lower().endswith(".png"):
                continue
            total += 1
            fpath = os.path.join(dirpath, fname)
            try:
                if fix_alpha_halo(fpath):
                    fixed += 1
                    print(f"[OK]   {fpath}")
                else:
                    skipped += 1
                    print(f"[SKIP] {fpath}")
            except Exception as exc:
                errors += 1
                print(f"[ERROR] {fpath}: {exc}")

    print()
    print(f"PNG trobats:    {total}")
    print(f"Corregits:      {fixed}")
    print(f"Sense canvis:   {skipped}")
    print(f"Errors:         {errors}")


if __name__ == "__main__":
    main()
