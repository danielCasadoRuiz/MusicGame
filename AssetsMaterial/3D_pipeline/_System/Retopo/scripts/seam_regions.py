"""Deterministic seam-region hints for oriented garments and a T-pose body.

Blender Z up, front -Y; shoes lengthwise X. These are spatial heuristics,
not semantic recognition of a garment or animation-ready retopology.
"""
def panel_label(profile, position, normal, lo, hi):
    size=[max(hi[i]-lo[i],1e-12) for i in range(3)]
    center=[(hi[i]+lo[i])*.5 for i in range(3)]
    x=(position[0]-center[0])/size[0];z=(position[2]-lo[2])/size[2]
    if profile=='trousers':
        label='waist' if z>.70 else 'leg_left' if x<0 else 'leg_right'
        if z>.97 and normal[2]>.65:label='waist_cap'
        if z<.04 and normal[2]<-.65:label='foot_cap_left' if x<0 else 'foot_cap_right'
    elif profile=='upper':
        label='torso'
        if abs(x)>.205 and z>.53:label='arm_left' if x<0 else 'arm_right'
        if abs(x)>.475:label='cuff_left' if x<0 else 'cuff_right'
        if z>.91 and abs(x)<.16:label='collar'
        if z<.025:label='hem'
    elif profile=='body':
        label='torso'
        if z<.48:label='leg_left' if x<0 else 'leg_right'
        if z>.65 and abs(x)>.20:label='arm_left' if x<0 else 'arm_right'
        if z>.88 and abs(x)<.15:label='head'
        if z<.08:label='foot_left' if x<0 else 'foot_right'
    elif profile=='shoe':
        label='sole' if z<.27 else 'upper'
        if z<.07:label='bottom'
    else:
        label='hair'
        if z<.025:label='bottom'
    if profile in ('trousers','upper','body','hood'):
        label+=':back' if position[1]>center[1] else ':front'
    elif profile=='shoe':
        label+=':medial' if position[1]>center[1] else ':lateral'
    return label
