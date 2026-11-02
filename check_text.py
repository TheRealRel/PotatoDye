#!/usr/bin/env python3
# Build check (Potato mods): a TextMeshPro text must not be added to a switched-on object, or the build stops.
# Adding a text component to an active GameObject runs its Awake inside AddComponent, before the code can set a
# font. Valheim ships no default font, so every such text logs "The LiberationSans SDF Font Asset was not found.
# There is no Font Asset assigned to <name>." (PotatoTweaks 1.5.20's chest contents row logged it 9 times.)
# The right way: create the object, SetActive(false), AddComponent, set font and material, then SetActive(true).
# The check:
#   - fails "new GameObject(...).AddComponent<Text type>" (nothing could have switched it off first);
#   - fails "new GameObject(..., typeof(Text type))" (the text wakes inside the constructor);
#   - fails "x.AddComponent<Text type>" unless "x.SetActive(false)" comes earlier in the same file.
# A line built under a parent that is already switched off can carry "// text-ok: <reason>" to pass.
# Runs at build time only; the mod itself carries nothing extra.
import re, sys, pathlib

TEXT = r"(?:TMPro\.)?(?:TextMeshProUGUI|TextMeshPro|TMP_Text|TMP_InputField|TMP_Dropdown|TMP_SubMeshUI|TMP_SubMesh)"
files = [pathlib.Path(p) for p in sys.argv[1:]] or sorted(pathlib.Path("src").glob("**/*.cs"))
problems = []
for f in files:
    src = f.read_text(encoding="utf-8", errors="replace")
    lines = src.splitlines()
    for n, line in enumerate(lines, 1):
        code = line.split("//")[0]
        if "text-ok:" in line:
            continue
        if re.search(r"new\s+GameObject\s*\([^;]*\)\s*\.\s*AddComponent\s*<\s*" + TEXT + r"\s*>", code):
            problems.append((f, n, "text added to a new, switched-on object"))
            continue
        if re.search(r"new\s+GameObject\s*\([^;]*typeof\s*\(\s*" + TEXT + r"\s*\)", code):
            problems.append((f, n, "text component passed to the GameObject constructor (wakes with no font)"))
            continue
        for m in re.finditer(r"([\w.]+?)(?:\.gameObject)?\s*\.\s*AddComponent\s*<\s*" + TEXT + r"\s*>", code):
            obj = m.group(1)
            before = "\n".join(l.split("//")[0] for l in lines[:n - 1]) + "\n" + code[:m.start()]
            off = re.search(re.escape(obj) + r"(?:\.gameObject)?\s*\.\s*SetActive\s*\(\s*false\s*\)", before)
            if not off:
                problems.append((f, n, f"text added to '{obj}' with no '{obj}.SetActive(false)' before it"))

for f, n, why in problems:
    print(f"{f}:{n}: {why}")
if problems:
    print("check_text: create the object switched off, add the text, set its font, then switch it on.")
    sys.exit(1)
print("check_text: no text is added to a switched-on object.")
