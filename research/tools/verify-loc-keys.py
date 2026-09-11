import json, io, os
root = r"G:/omp works/AutoAnthonyRelics"
def load(p):
    with io.open(p, encoding='utf-8-sig') as f: return json.load(f)
zhs = load(os.path.join(root, r"mod\QuriousCraftingRelics\localization\zhs\settings_ui.json"))
eng = load(os.path.join(root, r"mod\QuriousCraftingRelics\localization\eng\settings_ui.json"))
P = "QURIOUSCRAFTINGRELICS-"
rows = []
with io.open("slug-map.tsv", encoding='utf-8-sig') as f:
    for line in f:
        line = line.rstrip('\n')
        if not line.strip(): continue
        a,b,c,d = line.split('\t')
        rows.append((a,b,c,d))
broken = [r for r in rows if r[1] != r[3]]
fine   = [r for r in rows if r[1] == r[3]]
print("total=%d  broken_now=%d  already_fine=%d" % (len(rows), len(broken), len(fine)))
print("\n--- already fine (no rename needed) ---")
for a,b,c,d in fine: print("   %-34s slug=%s" % (a,b))
print("\n--- verification of the rename: does slug(new) hit an existing loc key? ---")
miss_zhs, miss_eng, ok = [], [], 0
for a,b,c,d in broken:
    k = P + d + ".title"
    inz, ine = k in zhs, k in eng
    if inz and ine: ok += 1
    else:
        if not inz: miss_zhs.append((a,c,k))
        if not ine: miss_eng.append((a,c,k))
print("rename resolves to an EXISTING loc key: %d / %d" % (ok, len(broken)))
for a,c,k in miss_zhs: print("  MISSING in zhs: %-34s -> %-34s (%s)" % (a,c,k))
for a,c,k in miss_eng: print("  MISSING in eng: %-34s -> %-34s (%s)" % (a,c,k))
print("\n--- hover.desc coverage after rename ---")
nh = [ (a,c) for a,b,c,d in broken if P+d+".hover.desc" not in zhs ]
print("broken rows lacking zhs hover.desc: %d" % len(nh))
for a,c in nh[:20]: print("   ", a, "->", c)
