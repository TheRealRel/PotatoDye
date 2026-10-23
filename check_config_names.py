# Stops a build when a BepInEx setting's section or key name has a character BepInEx rejects (= \n \t \ " ' [ ]).
# One apostrophe there makes the whole mod fail to load. Usage: python3 -I check_config_names.py src/*.cs
import re, sys

BAD = set('=\n\t\\"\'[]')
STRING = r'"((?:[^"\\]|\\.)*)"'
BIND = re.compile(r'\.Bind(?:<[^>]*>)?\(\s*(?:' + STRING + r'|([A-Za-z_][\w.]*))\s*,\s*' + STRING)
SECTION_CONST = re.compile(r'const string (\w*Section\w*)\s*=\s*' + STRING)


def unescape(text):
    return bytes(text, "utf-8").decode("unicode_escape")


problems = []
for path in sys.argv[1:]:
    text = open(path, encoding="utf-8").read()
    for m in BIND.finditer(text):
        names = [unescape(m.group(3))]
        if m.group(1) is not None:
            names.append(unescape(m.group(1)))
        for name in names:
            if any(c in BAD for c in name):
                line = text.count("\n", 0, m.start()) + 1
                problems.append(f"{path}:{line}: setting name {name!r} has a character BepInEx rejects")
    for m in SECTION_CONST.finditer(text):
        if any(c in BAD for c in unescape(m.group(2))):
            problems.append(f"{path}: section {m.group(2)!r} has a character BepInEx rejects")
if problems:
    print("\n".join(problems))
    sys.exit(1)
