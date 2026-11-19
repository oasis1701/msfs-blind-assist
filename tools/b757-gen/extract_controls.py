"""Map every interactive cockpit control of an installed MSFS aircraft package from its model behavior XML, read
only: the package's own templates are expanded, so each control's real RPN (what it reads, what a click writes, which
H:/K:/B: events it fires) is visible without the sim.

Usage:  python -I extract_controls.py <package-folder> <output-folder>
        (the package folder is the one holding manifest.json and layout.json, e.g. Community\\<vendor>-aircraft-757)
As a module (generate_b757_map.py does this): extract(package) -> (control rows, light rows, summary data): the rows the
        tables are made from, unclipped. A control row keeps its click codes as _clicks {slot: [(label, code)]}, its
        ANIMTIP texts as _tips [(n, text)], its state code as _state_code, its template parameters as _scope and the L:vars
        the whole component it sits in reads and writes as _component_vars {"reads": [name], "writes": [name]}.
Needs:  nothing running. Reads the package's files only; never executes, imports or writes inside the package.
How:    every model.cfg names an interior XML; it and each base it pulls in with <IncludeBase> are parsed with expat
        (comments kept: the vendor's banner comments name the panels). Templates defined in those files or in the
        package's ModelBehaviorDefs are expanded in place: <Parameters> and <DefaultTemplateParameters> set a value only
        when it is not set yet, <OverrideTemplateParameters> always set it, a <UseTemplate>'s children are set for its
        body, #PARAM# is substituted everywhere, <Condition Check/Valid/Test> picks its branch, and constant tests left
        by the substitution ("0 0 != if{ ... }") are folded away. The sim's own templates (ASOBO_GT_Switch_Code,
        ASOBO_GT_Knob_Infinite, ...) are not in the package and are not expanded: their parameters, inherited ones
        included, are the control (ANIM_CODE is its state, LEFT_SINGLE_CODE, CALLBACKCODE, CLOCKWISE_CODE ... its
        clicks); summary.md lists the assumptions this makes. If no interior has source behaviors (a modular 2024
        aircraft whose behaviors are compiled into *.behavior.xml), every ModelInfo and ModelBehaviors XML under
        SimObjects is read instead and the compiled files are only named.
Output (in <output-folder>, tables tab-separated, in file order; long cells end in "…"; the kinds are the ten of the
        investigation brief plus "select button" and "circuit breaker", defined in summary.md):
  control-map.tsv   one row per interactive control: file and line of the authored element, panel (banner), component,
                    node, template chain, kind, positions, state vars and code, labels (ANIMTIP_n in order, tooltip
                    texts, tooltip value code), click codes per mouse action, L:vars written, H:/K:/B: events, A: simvars,
                    Update code elsewhere that writes the same vars, notes for anything irregular
  lights.tsv        one row per emissive or visibility node that reads an L: or a non-lighting A: var: panel, node,
                    L:vars, circuits, potentiometers, the control it sits on (by name; "?" marks a guess), its code
  panels.md         per panel: controls by kind, lights, irregular controls, controls with no label
  summary.md        totals, interior files per variant, variable prefixes and suffixes, events, templates, what a
                    generator can derive mechanically, and the assumptions made about the sim's templates
The aircraft's own files are the vendor's: never commit them. These outputs are lists of names plus the short RPN of
each control (the vendor's variable names and logic, needed for interoperability), fine to commit as evidence.
"""
import collections
import json
import pathlib
import re
import sys
import xml.parsers.expat

CLIP = 400
PARAM_RE = re.compile(r"#([A-Za-z_][A-Za-z0-9_]*)#")
VAR_RE = re.compile(r"\((>?)\s*([A-Za-z]):([^,()]+?)\s*(?:,\s*([^()]*?))?\s*\)")
EVENT_BRANCH_RE = re.compile(r"\(M:Event\)\s*'(\w+)'\s*scm[pi]\s+0\s*==\s*if\{")
CONST_IF_RE = re.compile(r"(^|[{}]|\(>[^()]*\))(\s*)(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)\s+(==|!=|>=|<=|>|<)\s+if\{")
ANIMTIP_RE = re.compile(r"^ANIMTIP_(\d+)$")
TIP_KEYS = ("TOOLTIPID", "TOOLTIP_TITLE", "TOOLTIP", "DESCR", "TT_DESCRIPTION", "TT_DESCRIPTION_ID",
            "TOOLTIP_DESCRIPTION", "TOOLTIP_ENTRY_0")
VALUE_KEYS = ("VALUE", "TT_VALUE")
STATE_KEYS = ("ANIM_CODE", "OVERRIDE_ANIM_CODE")
LEFT_KEYS = ("LEFT_SINGLE_CODE",)
RELEASE_KEYS = ("LEFT_LEAVE_CODE", "LEFT_RELEASE_CODE", "LEFT_UP_CODE", "RELEASE_CODE", "LEFT_SINGLE_UP_CODE")
CW_KEYS = ("CLOCKWISE_CODE",)
CCW_KEYS = ("ANTICLOCKWISE_CODE",)
NOT_CLICK = re.compile(r"^(ANIM_CODE|OVERRIDE_ANIM_CODE|EMISSIVE_CODE|VISIBILITY_CODE|UPDATE_CODE|.*_EMISSIVE_CODE|"
                       r"CODE_POS_\d+_VERIF|STATE\d+_TEST|.*_FAILURE_CODE)$")
RELEASE_EVENTS = ("LeftRelease", "LeftUp", "Leave", "LeftLeave")
LIGHT_TEMPLATE = re.compile(r"Emissive|Visibility", re.I)
ANIM_TEMPLATE = re.compile(r"_Anim_Code$|_Anim_Sim$|_Anim_Lag|_Needle_Code$|_Anim$")
UPDATE_TEMPLATE = re.compile(r"_Update$")
INTERACTIVE_TEMPLATE = re.compile(r"Switch|Knob|Push|Button|Lever|Interaction|MouseRect|Yoke_Hideable|RudderPedals|"
                                  r"Tiller|Handle|Selector|Guard|Cover|FMC|CDU|Spoilers", re.I)
# Stock templates whose logic and state live inside the sim's library; the state named here is an assumption.
STOCK_STATE = {
    "ASOBO_HANDLING_Lever_Flaps_Template": "A:FLAPS HANDLE INDEX",
    "ASOBO_LANDING_GEAR_Lever_Gear_Template": "A:GEAR HANDLE POSITION",
    "ASOBO_HANDLING_Spoilers_Int_Template": "A:SPOILERS HANDLE POSITION",
    "ASOBO_ENGINE_Lever_Throttle_Template": "A:GENERAL ENG THROTTLE LEVER POSITION:#ID#",
    "ASOBO_HANDLING_RudderPedals_Template": "A:RUDDER PEDAL POSITION",
    "ASOBO_HANDLING_Steering_Tiller_Template": "A:STEER INPUT CONTROL",
    "ASOBO_HANDLING_Yoke_Hideable_Template": "A:YOKE X POSITION, A:YOKE Y POSITION",
    "ASOBO_HANDLING_Switch_ElevatorTrim_Template": "A:ELEVATOR TRIM POSITION",
    "ASOBO_INSTRUMENT_Knob_Altimeter_Template": "A:KOHLSMAN SETTING MB:#ID#",
    "ASOBO_AUTOPILOT_Knob_Altitude_Template": "A:AUTOPILOT ALTITUDE LOCK VAR",
    "ASOBO_AUTOPILOT_Knob_VerticalSpeed_Template": "A:AUTOPILOT VERTICAL HOLD VAR",
    "ASOBO_AUTOPILOT_Push_FlightDirector_Template": "A:AUTOPILOT FLIGHT DIRECTOR ACTIVE:#ID#",
    "ASOBO_AUTOPILOT_Push_Disconnect_Template": "A:AUTOPILOT MASTER",
    "ASOBO_LIGHTING_Switch_Light_Landing_Template": "A:LIGHT LANDING:#ID#",
    "ASOBO_LIGHTING_Switch_Light_Recognition_Template": "A:LIGHT RECOGNITION",
    "ASOBO_LIGHTING_Knob_Potentiometer_Template": "A:LIGHT POTENTIOMETER:#POTENTIOMETER#",
    "ASOBO_LIGHTING_Knob_Panel_Template": "A:LIGHT PANEL POWER SETTING",
    "ASOBO_LIGHTING_Knob_Cabin_Template": "A:LIGHT CABIN POWER SETTING",
    "ASOBO_BOEING_Switch_VOR_ADF_Template": "(stock RMI VOR/ADF pointer selection)",
}
GENERIC_TOKENS = {"PANEL", "LIGHTS", "LIGHT", "BUTTONS", "BUTTON", "SWITCHES", "SECTION", "MAIN", "CODE", "THE", "OF",
                  "AND", "CAPT", "CAPTAIN", "FO", "F", "O", "FIRST", "OFFICER", "START", "END", "BEGIN", "CONTROL"}
LIGHT_SUFFIXES = ("DECAL", "ON", "LIGHT", "LT", "LTS", "EMIS", "EMISSIVE", "INSCR", "BULB", "COMP", "VIS", "TEXT",
                  "LED", "GLOW", "BACKLIGHT", "ANNUN", "LEGEND")
GENERIC_TIPS = {"DECREASE", "INCREASE", "INC", "DEC", "ON", "OFF"}
KINDS = ("toggle", "multi-position switch", "momentary button", "held button", "select button", "rotary knob infinite",
         "rotary knob finite", "lever", "guard", "cdu key", "circuit breaker", "other")


class Comment(str):
    pass


class Node:
    __slots__ = ("tag", "attrib", "children", "line")

    def __init__(self, tag, attrib, line):
        self.tag, self.attrib, self.children, self.line = tag, attrib, [], line

    def elements(self):
        return [c for c in self.children if isinstance(c, Node)]


def parse_xml(path: pathlib.Path) -> Node:
    """A small tree with line numbers and comments, which ElementTree does not keep together."""
    parser = xml.parsers.expat.ParserCreate()
    root = Node("#document", {}, 1)
    stack = [root]

    def add_text(data):
        kids = stack[-1].children
        if kids and type(kids[-1]) is str:
            kids[-1] += data
        else:
            kids.append(data)

    def start(tag, attrs):
        node = Node(tag, attrs, parser.CurrentLineNumber)
        stack[-1].children.append(node)
        stack.append(node)

    parser.StartElementHandler = start
    parser.EndElementHandler = lambda tag: stack.pop()
    parser.CharacterDataHandler = add_text
    parser.CommentHandler = lambda data: stack[-1].children.append(Comment(data))
    parser.Parse(path.read_bytes(), True)
    return root


def text_of(node: Node) -> str:
    out = []
    for c in node.children:
        if isinstance(c, Comment):
            continue
        out.append(c if isinstance(c, str) else text_of(c))
    return "".join(out)


def squash(s: str) -> str:
    return " ".join(s.split())


def clip(s: str, n: int = CLIP) -> str:
    s = squash(s)
    return s if len(s) <= n else s[:n - 1] + "…"


def subst(s: str, scope: dict) -> str:
    for _ in range(6):
        new = PARAM_RE.sub(lambda m: scope.get(m.group(1), m.group(0)), s)
        if new == s:
            break
        s = new
    return s


def truthy(v) -> bool:
    return v is not None and v.strip().lower() not in ("", "false", "0", "0.0", "none")


def matching_brace(code: str, i: int) -> int:
    """Index just past the '}' closing the block whose '{' ends just before i, or len(code)."""
    depth = 1
    while i < len(code) and depth:
        if code[i] == "{":
            depth += 1
        elif code[i] == "}":
            depth -= 1
        i += 1
    return i


def fold_constants(code: str) -> str:
    """Drop the dead branch of 'a b op if{ } els{ }' when a and b are both literals after #PARAM# substitution."""
    for _ in range(30):
        m = CONST_IF_RE.search(code)
        if not m:
            break
        a, b, op = float(m.group(3)), float(m.group(4)), m.group(5)
        ok = {"==": a == b, "!=": a != b, ">=": a >= b, "<=": a <= b, ">": a > b, "<": a < b}[op]
        body_start = m.end()
        body_end = matching_brace(code, body_start)
        body = code[body_start:body_end - 1]
        rest = code[body_end:]
        els = re.match(r"\s*els\{", rest)
        other = ""
        if els:
            e_start = body_end + els.end()
            e_end = matching_brace(code, e_start)
            other = code[e_start:e_end - 1]
            rest = code[e_end:]
        keep = body if ok else other
        code = code[:m.start(3)] + " " + keep + " " + rest
    return code


def var_refs(code: str):
    """[(is_write, kind, name)] for every (X:name, unit) and (>X:name) in an RPN string."""
    out = []
    for m in VAR_RE.finditer(code or ""):
        kind, name = m.group(2).upper(), m.group(3).strip()
        if kind in ("M", "F", "P"):
            continue
        if kind == "K":
            name = re.sub(r"^\d+:", "", name)
        out.append((m.group(1) == ">", kind, name))
    return out


def is_click_key(k: str) -> bool:
    """A template parameter holding code that runs on a mouse or input action."""
    return (k.endswith("_CODE") or k.startswith("CALLBACKCODE") or bool(re.fullmatch(r"CODE_POS_\d+", k))) \
        and not NOT_CLICK.match(k)


def uniq(seq):
    seen, out = set(), []
    for x in seq:
        if x and x not in seen:
            seen.add(x)
            out.append(x)
    return out


def split_callback(code: str, flags: str):
    """{event: code} from a MouseRect CALLBACKCODE; a branch nested in an outer if{} keeps that test as a prefix."""
    out = collections.OrderedDict()
    found = list(EVENT_BRANCH_RE.finditer(code))
    for m in found:
        end = matching_brace(code, m.end())
        body = code[m.end():end - 1]
        depth, opener = 0, -1
        for j in range(m.start() - 1, -1, -1):
            if code[j] == "}":
                depth += 1
            elif code[j] == "{":
                if depth == 0:
                    opener = j
                    break
                depth -= 1
        if opener >= 0:
            prev = max(code.rfind("}", 0, opener), code.rfind("{", 0, opener))
            cond = code[prev + 1:opener].strip()
            cond = re.sub(r"\s*(if|els)$", "", cond).strip()
            if cond:
                body = f"[if {cond}] {body}"
        out[m.group(1)] = (out.get(m.group(1), "") + " " + body).strip()
    if not found and code.strip():
        events = [e for e in re.split(r"[+|]", flags or "") if e and e not in ("Lock",)]
        out["LeftSingle" if (not events or "LeftSingle" in events) else events[0]] = code
    return out


class Banners:
    """The vendor's banner comments as a stack: START/BEGIN opens a section, END closes the matching one, a plain
    banner names what follows until the next banner. What follows a closed section, before any new banner, is
    labeled "(after <section>)". A START written inside an element ends when that element closes, so a START the
    vendor never closed does not swallow the rest of the file."""

    def __init__(self, ancestors):
        self.stack, self.last, self.ancestors = [], None, ancestors  # stack: (label, is_start, parent element id)

    @staticmethod
    def tokens(label):
        return set(re.findall(r"[A-Z0-9]+", label.upper()))

    def prune(self) -> None:
        live = {id(a) for a in self.ancestors}
        for i, (label, is_start, parent) in enumerate(self.stack):
            if is_start and parent not in live:
                del self.stack[i:]
                self.stack.append((f"(after {label})", False, None))
                return

    def feed(self, text: str, parent: Node) -> None:
        t = text.strip()
        if "\n" in t or len(t) > 180 or "<" in t or ">" in t or not re.search(r"={3,}|#{4,}|\*{4,}|~{4,}|-{6,}", t):
            return
        label = re.sub(r"\s*(?:={3,}|#{4,}|\*{4,}|~{4,})\s*", " - ", t)
        label = squash(re.sub(r"^[\s=#*~\-\[\]]+|[\s=#*~\-\[\]]+$", "", label))
        label = re.sub(r"^MARK @#\d+\s*[-:]?\s*", "", label)
        if not label or label == self.last:
            return
        self.last = label
        self.prune()
        m = (re.match(r"^(?:START|BEGIN|BEGINNING)(?:\s+OF)?\b[\s:\-]*(.*)$", label, re.I)
             or re.match(r"^(.*?)[\s:\-]*\b(?:BEGIN|START)$", label, re.I))
        if m and m.group(1).strip():
            self.stack.append((m.group(1).strip(), True, id(parent)))
            return
        m = (re.match(r"^END(?:\s+OF)?\b[\s:\-]*(.*)$", label, re.I) or re.match(r"^(.*?)[\s:\-]*\bEND$", label, re.I))
        if m:
            self.close(m.group(1).strip(), id(parent))
            return
        self.stack.append((label, False, id(parent)))

    def close(self, name: str, parent: int) -> None:
        want = self.tokens(name)
        best, best_score = -1, 0.0
        for i in range(len(self.stack) - 1, -1, -1):
            have = self.tokens(self.stack[i][0])
            if want and have:
                score = len(want & have) / min(len(want), len(have))
                if score >= 0.6:
                    best = i
                    break
                if (want & have) - GENERIC_TOKENS and score > best_score:
                    best, best_score = i, score
        if best < 0 and self.stack and not self.stack[-1][1]:
            best = len(self.stack) - 1
        if best >= 0:
            label = self.stack[best][0]
            del self.stack[best:]
            self.stack.append((label if label.startswith("(after ") else f"(after {label})", False, parent))

    def panel(self) -> str:
        self.prune()
        return self.stack[-1][0] if self.stack else ""

    def path(self) -> str:
        self.prune()
        parts = [e[0] for e in self.stack if e[1]]
        if self.stack and not self.stack[-1][1]:
            parts.append(self.stack[-1][0])
        return " > ".join(parts)


class Frame:
    """One open <Component>: its ID and Node after substitution, whether the file wrote it (not a template), the template
    calls made inside it and the code of the Updates it holds."""

    def __init__(self, cid, node, authored):
        self.cid, self.node, self.authored = cid, node, authored
        self.parts = []
        self.updates = []


class Extractor:
    def __init__(self, pkg, loc):
        self.pkg, self.loc = pkg, loc
        self.controls, self.lights, self.updates = [], collections.OrderedDict(), []
        self.template_uses, self.leaf_uses = collections.Counter(), collections.Counter()
        self.unresolved_templates = collections.Counter()

    # ---- walking ---------------------------------------------------------------------------------------------
    def run(self, root: Node, templates: dict, rel: str) -> None:
        self.templates, self.rel = templates, rel
        self.ancestors = []
        self.banners = Banners(self.ancestors)
        self.frames = [Frame("(file root)", "", True)]
        self.chain, self.sites = [], []
        self.walk(root, {})
        self.close_frame(self.frames.pop())

    def apply_params(self, node: Node, scope: dict, override: bool) -> None:
        for p in node.elements():
            if p.tag in ("Condition", "Switch"):
                continue
            value = subst(text_of(p).strip(), scope)
            if value == f"#{p.tag}#":
                continue
            if override or p.tag not in scope:
                scope[p.tag] = value

    def walk(self, node: Node, scope: dict) -> None:
        self.ancestors.append(node)
        try:
            self.walk_children(node, scope)
        finally:
            self.ancestors.pop()

    def walk_children(self, node: Node, scope: dict) -> None:
        for child in node.children:
            if isinstance(child, Comment):
                if not self.chain:
                    self.banners.feed(child, node)
                continue
            if isinstance(child, str):
                continue
            tag = child.tag
            if tag in ("Template", "EditableTemplateParameters", "AnimationTriggers", "Include", "IncludeBase",
                       "LODS", "Sound", "Sounds"):
                continue
            if tag == "DefaultTemplateParameters" or (tag == "Parameters" and child.attrib.get("Type", "Default") != "Override"):
                self.apply_params(child, scope, override=False)
            elif tag in ("OverrideTemplateParameters", "Parameters"):
                self.apply_params(child, scope, override=True)
            elif tag == "Component":
                cid = subst(child.attrib.get("ID", ""), scope).strip()
                nid = subst(child.attrib.get("Node", ""), scope).strip()
                frame = Frame(cid, nid, not self.chain)
                self.frames.append(frame)
                self.walk(child, dict(scope))
                self.close_frame(self.frames.pop())
            elif tag == "UseTemplate":
                self.use_template(child, scope)
            elif tag == "Condition":
                branch = self.condition(child, scope)
                if branch is not None:
                    self.walk(branch, scope)
            elif tag == "Switch":
                self.switch(child, scope)
            elif tag == "Loop":
                self.loop(child, scope)
            elif tag == "Update":
                self.add_update(subst(text_of(child), scope), child.line)
            elif tag in ("Visibility", "Material"):
                codes = [subst(text_of(c), scope) for c in self.descendants(child) if c.tag == "Code"]
                frame = self.frames[-1]
                self.add_light("(raw " + tag + ")", frame.node or scope.get("NODE_ID", "") or frame.cid, " ".join(codes),
                               {}, child.line)
            elif tag == "MouseRect":
                params = {c.tag.upper(): subst(text_of(c), scope) for c in child.elements()}
                params["CALLBACKCODE"] = params.pop("CALLBACKCODE", "")
                self.add_leaf("MouseRect (raw)", dict(scope, **params), params, child.line)
            elif tag == "Animation":
                codes = [subst(text_of(c), scope) for c in self.descendants(child) if c.tag == "Code"]
                self.add_leaf("Animation (raw)", dict(scope, ANIM_CODE=" ".join(codes)), {}, child.line)
            else:
                self.walk(child, scope)

    @staticmethod
    def descendants(node: Node):
        for c in node.elements():
            yield c
            yield from Extractor.descendants(c)

    def use_template(self, node: Node, scope: dict) -> None:
        name = subst(node.attrib.get("Name", ""), scope).strip()
        params = collections.OrderedDict()
        for p in node.elements():
            params[p.tag] = subst(text_of(p).strip(), scope)
        inner = dict(scope)
        inner.update(params)
        if name == "ASOBO_GT_Helper_Suffix_ID_Appender" and inner.get("TEMPLATE_TO_CALL", "") in self.templates:
            name = inner["TEMPLATE_TO_CALL"]
        if name in self.templates and self.chain.count(name) < 2 and len(self.chain) < 25:
            self.template_uses[name] += 1
            self.chain.append(name)
            self.sites.append(node.line)
            self.walk(self.templates[name], inner)
            self.chain.pop()
            self.sites.pop()
        else:
            if PARAM_RE.search(name):
                self.unresolved_templates[name] += 1
            self.leaf_uses[name] += 1
            self.add_leaf(name, inner, params, node.line)

    def condition(self, node: Node, scope: dict):
        attrs = node.attrib
        kids = node.elements()
        if "Check" in attrs:
            ok = subst(attrs["Check"], scope) in scope
        elif "Valid" in attrs:
            ok = truthy(scope.get(subst(attrs["Valid"], scope)))
        elif "NotEmpty" in attrs:
            ok = scope.get(subst(attrs["NotEmpty"], scope), "").strip() != ""
        else:
            test = next((k for k in kids if k.tag == "Test"), None)
            ok = self.test(test.elements()[0], scope) if test is not None and test.elements() else True
        true_b = next((k for k in kids if k.tag == "True"), None)
        false_b = next((k for k in kids if k.tag == "False"), None)
        if true_b is None and false_b is None:
            if not ok:
                return None
            body = Node("#branch", {}, node.line)
            body.children = [c for c in node.children if not (isinstance(c, Node) and c.tag == "Test")]
            return body
        return true_b if ok else false_b

    def value(self, node: Node, scope: dict):
        raw = subst(text_of(node).strip(), scope)
        if raw in scope:
            return scope[raw]
        if re.fullmatch(r"[A-Z_][A-Z0-9_]*", raw):
            return None
        return raw

    def test(self, node: Node, scope: dict) -> bool:
        kids = node.elements()
        if node.tag == "And":
            return all(self.test(k, scope) for k in kids)
        if node.tag == "Or":
            return any(self.test(k, scope) for k in kids)
        if node.tag == "Not":
            return not self.test(kids[0], scope) if kids else True
        if node.tag in ("Value", "Arg"):
            return truthy(self.value(node, scope))
        if node.tag in ("Defined", "Check"):
            return subst(text_of(node).strip(), scope) in scope
        if node.tag in ("Greater", "Lower", "Equal", "NotEqual", "GreaterEqual", "LowerEqual") and len(kids) >= 2:
            a, b = self.value(kids[0], scope), self.value(kids[1], scope)
            try:
                a, b = float(a), float(b)
            except (TypeError, ValueError):
                a, b = str(a), str(b)
            return {"Greater": a > b, "Lower": a < b, "Equal": a == b, "NotEqual": a != b,
                    "GreaterEqual": a >= b, "LowerEqual": a <= b}[node.tag]
        return True

    def switch(self, node: Node, scope: dict) -> None:
        param = subst(node.attrib.get("Param", ""), scope)
        for case in node.elements():
            if case.tag == "Case":
                if "Value" in case.attrib:
                    hit = scope.get(param) == subst(case.attrib["Value"], scope)
                elif "Check" in case.attrib:
                    hit = subst(case.attrib["Check"], scope) in scope
                elif "Valid" in case.attrib:
                    hit = truthy(scope.get(subst(case.attrib["Valid"], scope)))
                else:
                    hit = False
                if hit:
                    self.walk(case, scope)
                    return
            elif case.tag == "Default":
                self.walk(case, scope)
                return

    def loop(self, node: Node, scope: dict) -> None:
        setup = next((k for k in node.elements() if k.tag == "Setup"), None)
        body = next((k for k in node.elements() if k.tag == "Do"), None)
        if setup is None or body is None:
            return
        cfg = {k.tag: subst(text_of(k).strip(), scope) for k in setup.elements()}
        try:
            i, inc, to = float(cfg.get("From", 0)), float(cfg.get("Inc", 1)), float(cfg.get("To", 0))
        except ValueError:
            return
        n = 0
        while (i <= to if inc > 0 else i >= to) and n < 64 and inc:
            scope[cfg.get("Param", "I")] = str(int(i)) if i == int(i) else str(i)
            self.walk(body, scope)
            i += inc
            n += 1

    # ---- collecting ------------------------------------------------------------------------------------------
    def add_update(self, code: str, line: int) -> None:
        frame = self.authored_frame()
        frame.updates.append(code)  # all of it, reads too: component_vars
        writes = [n for w, k, n in var_refs(code) if w and k == "L"]
        if writes:
            init = bool(re.search(r"IsInit|INIT", code)) and code.count("if{") <= 2
            self.updates.append((frame.cid, writes, clip(code, 200), init))

    def authored_frame(self) -> Frame:
        for f in reversed(self.frames):
            if f.authored:
                return f
        return self.frames[0]

    def add_leaf(self, name: str, scope: dict, explicit: dict, line: int) -> None:
        frame = self.frames[-1]
        own_nodes = [v for k, v in explicit.items() if k.startswith("NODE_ID") and v.strip()]
        node = explicit.get("NODE_ID") or frame.node or (own_nodes[0] if own_nodes else "") or scope.get("NODE_ID", "")
        real_node = bool(node.strip())
        node = (node or frame.cid).strip()
        if UPDATE_TEMPLATE.search(name):
            self.add_update(scope.get("UPDATE_CODE", ""), line)
            return
        if LIGHT_TEMPLATE.search(name) and not re.search(r"Switch|Knob|Push|Lever", name):
            code = " ".join(scope.get(k, "") for k in ("EMISSIVE_CODE", "VISIBILITY_CODE", "FAILURE", "SEQ1_EMISSIVE_CODE",
                                                          "SEQ2_EMISSIVE_CODE") if scope.get(k))
            self.add_light(name, node, code, scope, line)
            return
        codes = {k: v for k, v in scope.items() if is_click_key(k) and v.strip()}
        interactive = name not in ("Animation (raw)",) and not ANIM_TEMPLATE.search(name) and (
            bool(codes) or bool(scope.get("SWITCH_POSITION_VAR", "").strip()) or name in STOCK_STATE
            or (INTERACTIVE_TEMPLATE.search(name) and not re.match(r"ASOBO_GT_(Switch_Code|Push_Button)", name)))
        if not interactive:  # an animated part (its state may belong to a control on the same node) or a gauge
            if real_node and (ANIM_TEMPLATE.search(name) or name == "Animation (raw)"
                              or re.match(r"ASOBO_GT_(Switch|Push)", name)):
                frame.parts.append({"anim": True, "node": node, "state": scope.get("ANIM_CODE", ""), "name": name})
            return
        frame.parts.append({"anim": False, "node": node, "name": name, "scope": scope, "explicit": explicit,
                            "line": self.sites[0] if self.sites else line, "chain": tuple(self.chain),
                            "panel": self.banners.panel(), "path": self.banners.path(),
                            "component": self.authored_frame().cid})

    def add_light(self, source: str, node: str, code: str, scope: dict, line: int) -> None:
        refs = var_refs(code)
        lvars = [n for w, k, n in refs if k == "L" and not w]
        avars = [n for w, k, n in refs if k == "A" and not w]
        circuits = [re.sub(r"\s+", " ", n) for n in avars if n.upper().startswith(("CIRCUIT", "ELECTRICAL", "BUS"))]
        pots = [n for n in avars if n.upper().startswith("LIGHT POTENTIOMETER")]
        if scope.get("POTENTIOMETER", "").strip():
            pots.append("LIGHT POTENTIOMETER:" + scope["POTENTIOMETER"].strip())
        others = [n for n in avars if n not in circuits and n not in pots]
        key = (self.rel, node)
        row = self.lights.get(key)
        if row is None:
            row = self.lights[key] = {"file": self.rel, "line": self.sites[0] if self.sites else line,
                                      "panel": self.banners.panel(), "path": self.banners.path(),
                                      "components": [], "node": node, "sources": [], "lvars": [], "circuits": [],
                                      "pots": [], "others": [], "code": []}
        row["components"] = uniq(row["components"] + [self.authored_frame().cid])
        row["sources"] = uniq(row["sources"] + [source])
        for k, v in (("lvars", lvars), ("circuits", circuits), ("pots", pots), ("others", others)):
            row[k] = uniq(row[k] + v)
        if code.strip():
            row["code"] = uniq(row["code"] + [squash(code)])

    @staticmethod
    def component_vars(frame: Frame) -> dict:
        """The L:vars the whole component reads and writes in its own code, each in order of first appearance: its animations'
        codes, every template parameter of its interactions (their click codes and their ANIM_CODE) and the code of its
        Updates. A control's row holds the code of its own node only, and a lever's position var can be set by an Update and
        shown by ANOTHER node's animation of the same component (the gear lever's rotation: L:B757_GEAR_HANDLE_POS), which no
        code of the node the pilot grabs mentions."""
        codes = [part["state"] if part["anim"] else " ".join(part["scope"].values()) for part in frame.parts]
        refs = [(is_write, name) for code in codes + frame.updates for is_write, kind, name in var_refs(code) if kind == "L"]
        return {"reads": uniq(name for is_write, name in refs if not is_write),
                "writes": uniq(name for is_write, name in refs if is_write)}

    def close_frame(self, frame: Frame) -> None:
        groups = collections.OrderedDict()
        for part in frame.parts:
            groups.setdefault(part["node"], []).append(part)
        component = self.component_vars(frame) if groups else {}
        for node, parts in groups.items():
            live = [p for p in parts if not p["anim"]]
            if live:
                row = self.make_row(node, live, [p for p in parts if p["anim"]])
                row["_component_vars"] = {kind: list(names) for kind, names in component.items()}
                self.controls.append(row)

    # ---- one control -----------------------------------------------------------------------------------------
    def make_row(self, node: str, live: list, anims: list) -> dict:
        first = live[0]
        scope = {}
        for p in live:
            for k, v in p["scope"].items():
                if k not in scope or (v.strip() and not scope[k].strip()):
                    scope[k] = v
        names = [p["name"] for p in live]
        clicks = collections.OrderedDict((k, []) for k in ("left", "release", "cw", "ccw", "other"))

        def given(k):  # a parameter's value, unless it is empty or a placeholder nobody filled in
            v = scope.get(k, "").strip()
            return v if v and not PARAM_RE.fullmatch(v) and v not in ("''", "'N/A'") else ""

        def put(slot, label, code):
            code = squash(fold_constants(code))
            if code and (label, code) not in clicks[slot]:
                clicks[slot].append((label, code))

        for p in live:
            ps = p["scope"]
            for k in sorted(k for k in ps if is_click_key(k)):
                v = ps[k]
                if not v.strip():
                    continue
                if k.startswith("CALLBACKCODE"):
                    suffix = k[len("CALLBACKCODE"):]
                    flags = ps.get("MOUSEFLAGS" + suffix) or ps.get("MOUSEFLAGS") or ps.get("MouseFlags", "")
                    for event, body in split_callback(fold_constants(v), flags).items():
                        slot = "left" if event == "LeftSingle" else "release" if event in RELEASE_EVENTS else "other"
                        put(slot, event if slot == "other" else "", body)
                elif k in LEFT_KEYS:
                    put("left", "", v)
                elif k in RELEASE_KEYS:
                    put("release", "" if k == "LEFT_LEAVE_CODE" else k, v)
                elif k in CW_KEYS:
                    put("cw", "", v)
                elif k in CCW_KEYS:
                    put("ccw", "", v)
                else:
                    put("other", k, v)
        state_code = squash(" ".join(uniq([scope.get(k, "") for k in STATE_KEYS if scope.get(k, "").strip()][:1]
                                          or [a["state"] for a in anims if a["state"].strip()][:2])))
        state_refs = var_refs(state_code)
        state_vars = uniq(f"{k}:{n}" for w, k, n in state_refs if k in ("L", "A", "O"))
        implicit_writes = []
        pos_var, pos_type = scope.get("SWITCH_POSITION_VAR", "").strip(), scope.get("SWITCH_POSITION_TYPE", "L").strip()
        if pos_var and any("Switch" in n for n in names):  # ASOBO_GT_Switch_3States writes the position var itself
            v = f"{pos_type or 'L'}:{pos_var}"
            state_vars = uniq([v] + state_vars)
            implicit_writes.append(v + " (by template)")
        for k in ("SWITCH_VAR_NAME", "COVER_VAR_NAME", "VALUE_LVAR"):  # stock covered switches and finite knobs
            if given(k) and (k != "VALUE_LVAR" or k in first["explicit"]):
                state_vars = uniq(state_vars + ["L:" + given(k)])
        stock = next((n for n in names if n in STOCK_STATE), "")
        if stock and not state_vars:
            state_vars = [subst(STOCK_STATE[stock], scope) + " (assumed: inside stock template)"]

        all_click = " ".join(c for slot in clicks.values() for _, c in slot)
        click_refs = var_refs(all_click)
        writes = uniq([f"L:{n}" for w, k, n in click_refs if w and k == "L"])
        writes += [w for w in implicit_writes if w.startswith("L:") and w.split(" ")[0] not in writes]
        events = uniq(f"{k}:{n}" for w, k, n in click_refs if w and k in ("K", "H", "B"))
        value_code = squash(" ".join(given(k) for k in VALUE_KEYS if given(k)))
        simvars = uniq(f"A:{n}" for w, k, n in var_refs(state_code + " " + all_click + " " + value_code) if k == "A")
        a_writes = uniq(f"A:{n}" for w, k, n in click_refs if w and k == "A")
        o_writes = uniq(f"O:{n}" for w, k, n in click_refs if w and k == "O")

        tips = sorted(((int(m.group(1)), self.text(given(k))) for k in scope
                       if (m := ANIMTIP_RE.match(k)) and given(k)), key=lambda t: t[0])
        animtips = " | ".join(f"{i}={t}" for i, t in tips)
        tooltip = "; ".join(uniq(f"{k}={self.text(given(k))}" for k in TIP_KEYS if given(k)))
        kind = self.classify(names, first["chain"], scope, clicks, node, state_vars, writes, tips)
        if kind in ("toggle", "guard") and not state_vars:
            m = re.search(r"\(L:([^,()]+?)\s*(?:,[^()]*)?\)\s*!\s*\(>L:\1\s*[,)]", " ".join(c for _, c in clicks["left"]))
            if m:
                state_vars = [f"L:{m.group(1)} (from the click; no ANIM_CODE)"]
        if kind.startswith("rotary knob") and not state_vars:
            own = [w for w in writes if w[2:].upper() == node.upper().replace("-", "_")]
            if own:
                state_vars = [f"{own[0]} (the knob's own position, from the turn code; no ANIM_CODE)"]
        positions = self.positions(kind, clicks, scope, tips)

        notes = []
        if not any(v.startswith("L:") for v in state_vars):
            notes.append("state not an L:var" + (f" ({', '.join(state_vars)})" if state_vars else " (none)"))
        bases = [v.split(":", 1)[1].split(" ")[0] for v in state_vars if v.startswith("L:")] + [node]
        foreign = [w for w in writes if not any(w[2:].startswith(b) or b.startswith(w[2:].split(" ")[0])
                                                for b in bases if b)]
        if kind == "select button":
            notes.append(f"selects one of a group (clears {len(writes) - 1} other L:vars)")
        elif foreign:
            notes.append(f"writes L:vars unrelated to its state ({len(foreign)} of {len(writes)})")
        left_code = " ".join(c for _, c in clicks["left"])
        if kind == "momentary button" and not clicks["release"] and re.search(r"\d\s*\(>L:", left_code):
            notes.append("press only: sets a value, no release code")
        stepped = re.findall(r"(?:^|\s)-?1\s*\(>L:([^,()]+?)\s*[,)]", " ".join(c for _, c in clicks["cw"] + clicks["ccw"]))
        if any(not any(s.startswith("L:" + v) for s in state_vars) for v in stepped):
            notes.append("turn writes +1/-1 to a request var")
        for prefix, label in (("H:", "H: event"), ("K:", "K: event"), ("B:", "B: event")):
            if any(e.startswith(prefix) for e in events):
                notes.append(label)
        if a_writes:
            notes.append("writes A: " + ", ".join(a_writes))
        if o_writes and not writes:
            notes.append("click writes only O: (component-local) vars")
        if "mask" in state_code.lower() or "^" in all_click:
            notes.append("bitmask state")
        if "[if " in all_click:
            notes.append("click gated by " + "; ".join(uniq(re.findall(r"\[if ([^\]]+)\]", all_click)))[:120])
        if stock:
            notes.append("stock Asobo template: its logic is not in the package")
        if re.search(r"Yoke|RudderPedals|Tiller", " ".join(names)):
            notes.append("flight control")
        unresolved = uniq(PARAM_RE.findall(all_click + " " + state_code))
        if unresolved:
            notes.append("unresolved #" + "#, #".join(unresolved) + "#")
        if kind == "other" and not re.search(r"Yoke|RudderPedals|Tiller", " ".join(names)):
            notes.append("kind not recognized")
        if scope.get("IE_NAME", "").strip():
            notes.append("input event " + scope["IE_NAME"].strip())
        if len(live) > 1 and len(set(names)) > 1:
            notes.append("several interaction templates on one node")

        def cell(slot):
            return clip(" || ".join(f"{label}: {code}" if label else code for label, code in clicks[slot]))

        return {"file": self.rel, "line": first["line"], "panel": first["panel"] or "(no banner)", "section": first["path"],
                "component": first["component"], "node": node,
                "template": " > ".join(list(first["chain"]) + ["+".join(uniq(names))]), "kind": kind,
                "positions": positions, "state_vars": ", ".join(state_vars), "state_code": clip(state_code, 200),
                "animtips": clip(animtips, 300), "tooltip": clip(tooltip, 300), "tooltip_value": clip(value_code, 200),
                "left": cell("left"), "release": cell("release"), "cw": cell("cw"), "ccw": cell("ccw"),
                "other": cell("other"), "writes": clip(", ".join(writes), 300), "events": ", ".join(events),
                "simvars": clip(", ".join(simvars), 200), "notes": notes, "_writes": writes, "_state": state_vars,
                "_labeled": any(given(k) for k in ("TOOLTIPID", "TOOLTIP_TITLE", "TOOLTIP", "TOOLTIP_ENTRY_0"))
                or any(t.strip().upper() not in GENERIC_TIPS for _, t in tips),
                # unclipped, for a generator: the click codes as [(label, code)] per slot, the ANIMTIP_n texts as
                # [(n, text)], the state code and the template's parameters (hold time, switch position var ...)
                "_clicks": {slot: list(items) for slot, items in clicks.items()}, "_tips": tips,
                "_state_code": state_code, "_scope": dict(scope)}

    def text(self, value: str) -> str:
        v = value.strip()
        if len(v) >= 2 and v[0] == v[-1] == "'":
            v = v[1:-1]
        key = v
        for prefix in ("TT:", "@"):
            if key.startswith(prefix):
                key = key[len(prefix):]
        word = self.loc.get(key, "")
        return f"{v} ({word})" if word and word != v else v

    @staticmethod
    def classify(names, chain, scope, clicks, node, state_vars, writes, tips) -> str:
        joined = " ".join(names)
        everything = " ".join(list(chain) + names)
        ident = " ".join([node] + state_vars + writes).upper()
        left = " ".join(c for _, c in clicks["left"])
        release = " ".join(c for _, c in clicks["release"])
        turn = " ".join(c for _, c in clicks["cw"] + clicks["ccw"])
        other = " ".join(c for _, c in clicks["other"])
        interaction = (scope.get("INTERACTION_TYPE", "") or "").lower()
        if re.search(r"Yoke|RudderPedals|Tiller", joined):
            return "other"
        if re.search(r"CDU_KEY|PushButton_Held_Template|ASOBO_FMC", everything) or (
                re.search(r"(^|[_\s])(CDU|FMC|MCDU)(_|\d)", ident) and (release or "Push" in joined)):
            return "cdu key"
        if re.search(r"GUARD|GAURD|COVER", ident) and not re.search(r"Lever|Knob", joined):
            return "guard"
        if re.search(r"Lever|DraggingAxis|Dragging|Throttle|Flaps|Spoilers", joined) or scope.get("DRAG_CODE", "").strip():
            return "lever"
        if "^" in left:
            return "circuit breaker"
        all_codes = left + " " + turn + " " + other
        if re.search(r"Knob", everything) or turn or interaction == "knob":
            wrap = (truthy(scope.get("WRAP")) or truthy(scope.get("ANIM_WRAP")) or "Infinite" in joined
                    or re.search(r"\d\s*%", all_codes) or re.search(r"AUTOPILOT_Knob|INSTRUMENT_Knob", joined)
                    or any(a != b for a, b in re.findall(r"\bd\s+(\d+)\s*[<>]\s*if\{\s*(\d+)\s*\}", all_codes)))
            if not turn and not left and not other and "Knob" not in joined:
                return "other"
            return "rotary knob infinite" if wrap else "rotary knob finite"
        if "3States" in joined or len(tips) >= 3:
            return "multi-position switch"
        if truthy(scope.get("FIRE_HELD_EVENT")) and scope.get("MIN_HELD_DURATION", "").strip():
            return "held button"
        sets = re.findall(r"(?:^|\s)(-?\d+(?:\.\d+)?)\s*\(>L:([^,()]+?)\s*[,)]", left)
        own = {v.split(":", 1)[1].split(" ")[0] for v in state_vars if v.startswith("L:")}
        if not release.strip() and any(v in own and n == "1" for n, v in sets) and sum(n == "0" for n, v in sets) >= 2:
            return "select button"
        toggle = (re.search(r"\(L:([^,()]+)[^()]*\)\s*!\s*\(>L:\1\s*[,)]", left)
                  or re.search(r"\(L:([^,()]+)[^()]*\)\s*\d+\s*==\s*if\{\s*0\s*\}\s*els\{\s*1\s*\}\s*\(>L:\1\s*[,)]", left)
                  or re.search(r"\(L:([^,()]+)[^()]*\)\s*!\s*\(>L:\1\s*[,)]", other))
        if release.strip() or ("Push" in joined and not toggle) or interaction == "push":
            return "momentary button"
        if toggle:
            return "toggle"
        if re.search(r"\d\s+(min|max)\s*\(>L:", left + " " + other) or re.search(r"\+\s*\d+\s*%", left):
            return "multi-position switch"
        reads = {n for w, k, n in var_refs(left) if k == "L" and not w}
        written = {n for w, k, n in var_refs(left) if k == "L" and w}
        if reads & written:
            return "toggle" if len(tips) <= 2 else "multi-position switch"
        if re.search(r"\(>[LKHB]:", left) or re.search(r"\(>[KHB]:", other):
            return "momentary button"
        if re.search(r"Switch", joined):
            return "toggle"
        if re.search(r"Push|Button", joined):
            return "momentary button"
        return "other"

    @staticmethod
    def positions(kind, clicks, scope, tips) -> str:
        if kind in ("toggle", "guard", "circuit breaker"):
            return "2"
        if kind in ("momentary button", "held button", "cdu key", "select button"):
            return "-"
        if kind == "rotary knob infinite":
            return "inf"
        turns = [c for _, c in clicks["cw"] + clicks["ccw"]]
        constant = [re.fullmatch(r"\s*(-?\d+(?:\.\d+)?)\s*\(>L:[^()]*\)\s*", c) for c in turns]
        if turns and all(constant):
            return str(len({m.group(1) for m in constant}))
        code = " ".join(c for slot in ("left", "cw", "ccw", "other") for _, c in clicks[slot])
        hi = [float(x) for x in re.findall(r"(-?\d+(?:\.\d+)?)\s+min\b", code)]
        lo = [float(x) for x in re.findall(r"(-?\d+(?:\.\d+)?)\s+max\b", code)]
        hi += [float(a) for a, b in re.findall(r"\bd\s+(\d+)\s*>\s*if\{\s*(\d+)\s*\}", code) if a == b]
        lo += [float(a) for a, b in re.findall(r"\bd\s+(\d+)\s*<\s*if\{\s*(\d+)\s*\}", code) if a == b]
        steps = [float(x) for x in re.findall(r"\)\s+(\d+(?:\.\d+)?)\s+[+\-]\s", code)]
        if "++" in code or "--" in code:
            steps.append(1.0)
        for k_lo, k_hi in (("VAR_MIN", "VAR_MAX"), ("MIN_VALUE", "MAX_VALUE")):
            try:
                lo.append(float(scope[k_lo]))
                hi.append(float(scope[k_hi]))
            except (KeyError, ValueError):
                pass
        if kind == "multi-position switch" and len(tips) >= 3:
            return str(len(tips))
        if kind == "multi-position switch" and scope.get("SWITCH_POSITION_VAR", "").strip():
            return "3"  # ASOBO_GT_Switch_3States
        if hi:
            top, bottom = max(hi), (min(lo) if lo else 0.0)
            step = min(steps) if steps else 1.0
            if step > 0 and top > bottom:
                n = (top - bottom) / step + 1
                if n <= 12 and abs(n - round(n)) < 1e-6:
                    return str(int(round(n)))
                return f"range {bottom:g}..{top:g} step {step:g}"
        if len(tips) >= 3:
            return str(len(tips))
        return ""


# ---- package-level -------------------------------------------------------------------------------------------
def read_text(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8-sig", errors="replace")


def load_loc(pkg: pathlib.Path) -> dict:
    """The package's en-US strings (locPak is JSON), so TT: keys and @ keys read as words."""
    for name in ("en-US.locPak", "en-GB.locPak"):
        p = pkg / name
        if p.is_file():
            try:
                return json.loads(read_text(p)).get("LocalisationPackage", {}).get("Strings", {})
            except (ValueError, AttributeError):
                pass
    return {}


def rel(path: pathlib.Path, pkg: pathlib.Path) -> str:
    try:
        return path.relative_to(pkg).as_posix()
    except ValueError:
        return path.as_posix()


def parse_cfg(text: str) -> list:
    """[(section, key, value)] from an MSFS .cfg, comments stripped, quotes removed."""
    out, section = [], ""
    for line in text.splitlines():
        line = line.split(";", 1)[0].strip()
        if line.startswith("[") and "]" in line:
            section = line[1:line.index("]")]
        elif "=" in line:
            key, value = line.split("=", 1)
            out.append((section.lower(), key.strip().lower(), value.strip().strip('"')))
    return out


def resolve_path(base: pathlib.Path, value: str):
    p = (base / value.replace("\\", "/")).resolve()
    if p.is_file():
        return p
    low = {c.name.lower(): c for c in p.parent.iterdir()} if p.parent.is_dir() else {}
    return low.get(p.name.lower())


def write_tsv(path: pathlib.Path, header: list, rows: list) -> None:
    with path.open("w", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(header) + "\n")
        for row in rows:
            f.write("\t".join(str(c).replace("\t", " ").replace("\n", " ") for c in row) + "\n")


def collect_templates(root: Node, into: dict) -> None:
    for c in root.elements():
        if c.tag == "Template" and c.attrib.get("Name"):
            into[c.attrib["Name"]] = c
        collect_templates(c, into)


def find_children(root: Node, tag: str):
    for c in Extractor.descendants(root):
        if c.tag == tag:
            yield c


def note_key(note: str) -> str:
    """A note without its details, for counting: 'state not an L:var (O:_AnimProgress)' -> 'state not an L:var'."""
    if re.match(r"^[HKB]: event", note):
        return note
    for prefix in ("input event", "unresolved", "selects one of a group", "writes A:"):
        if note.startswith(prefix):
            return prefix
    return re.split(r" \(| by |: ", note)[0]


def prefix_of(name: str) -> str:
    parts = name.split("_")
    if len(parts) >= 3:
        return "_".join(parts[:2]) + "_"
    return parts[0] + "_" if len(parts) == 2 else name


SUFFIXES = ("_PRESS", "_ON", "_DECAL", "_BULB_FAILED", "_HELD", "_STATE", "_LIGHT", "_BUTTON", "_SWITCH", "_KNOB",
            "_GUARD", "_COVER", "_SW", "_SEL")


def extract(pkg: pathlib.Path):
    """Read the package: (control rows, light rows, summary data), unclipped, in file order. A control row holds the
    TSV's cells (clipped) and, under underscore keys, the unclipped click codes (_clicks), ANIMTIP texts (_tips), state
    code and template parameters (_scope) and the L:vars its whole component reads and writes (_component_vars);
    summary holds what write_summary reports (manifest, interior files, template use, errors)."""
    sys.setrecursionlimit(max(sys.getrecursionlimit(), 10000))  # deeply nested vendor templates
    loc = load_loc(pkg)
    errors = []
    parsed = {}

    def load(path: pathlib.Path):
        if path not in parsed:
            try:
                parsed[path] = parse_xml(path)
            except (xml.parsers.expat.ExpatError, OSError) as e:
                errors.append(f"{rel(path, pkg)}: {e}")
                parsed[path] = None
        return parsed[path]

    library = {}
    mbd = pkg / "ModelBehaviorDefs"
    for path in sorted(mbd.rglob("*.xml")) if mbd.is_dir() else []:
        root = load(path)
        if root is not None:
            collect_templates(root, library)

    used_by = collections.defaultdict(set)  # interior file -> model folders
    roles = {}
    for cfg in sorted((pkg / "SimObjects").rglob("model.cfg")) if (pkg / "SimObjects").is_dir() else []:
        for section, key, value in parse_cfg(read_text(cfg)):
            if section == "models" and key in ("interior", "exterior") and value:
                target = resolve_path(cfg.parent, value)
                if target is not None:
                    roles.setdefault(target, key)
                    if key == "interior":
                        used_by[target].add(rel(cfg.parent, pkg))

    interiors = [p for p, r in roles.items() if r == "interior"]
    bases = {}  # file -> its IncludeBase files

    def chain_bases(path, seen=()):
        root = load(path)
        if root is None or path in seen:
            return []
        found = []
        for inc in find_children(root, "IncludeBase"):
            target = resolve_path(path.parent, inc.attrib.get("RelativeFile", ""))
            if target is not None:
                found.append(target)
                found += chain_bases(target, seen + (path,))
        bases[path] = found
        return found

    order = []
    for path in sorted(interiors):
        for b in reversed(chain_bases(path)):
            if b not in order:
                order.append(b)
                roles[b] = "interior base"
        if path not in order:
            order.append(path)

    def has_behaviors(path):
        root = load(path)
        return root is not None and any(c.tag in ("Behaviors", "ModelBehaviors") for c in Extractor.descendants(root))

    def compiled(path):
        root = load(path)
        return root is not None and any(c.tag == "Behaviors" and c.attrib.get("Compiled", "").lower() == "true"
                                        for c in Extractor.descendants(root))

    compiled_interiors = [p for p in order if compiled(p)]
    fallback = False
    if not any(has_behaviors(p) and list(find_children(parsed[p], "Component")) for p in order):
        fallback = True
        order = []
        for path in sorted((pkg / "SimObjects").rglob("*.xml")) if (pkg / "SimObjects").is_dir() else []:
            if path.name.lower().endswith(".behavior.xml"):
                continue
            root = load(path)
            if root is None or not has_behaviors(path):
                continue
            if any(c.tag == "ModelInfo" for c in root.elements()):
                if compiled(path):
                    compiled_interiors.append(path)
                order.append(path)
                roles.setdefault(path, "model (fallback)")
            elif any(c.tag == "ModelBehaviors" for c in root.elements()) and list(find_children(root, "Component")):
                order.append(path)
                roles.setdefault(path, "behavior file (fallback)")

    ex = Extractor(pkg, loc)
    file_stats = []
    externals = collections.Counter()
    defined_local = set(library)
    for path in order:
        root = parsed.get(path) or load(path)
        if root is None:
            continue
        templates = dict(library)
        for b in reversed(bases.get(path, [])):
            if parsed.get(b) is not None:
                collect_templates(parsed[b], templates)
        own = {}
        collect_templates(root, own)
        templates.update(own)
        defined_local |= set(own)
        for inc in find_children(root, "Include"):
            ref = inc.attrib.get("ModelBehaviorFile") or inc.attrib.get("Path") or inc.attrib.get("RelativeFile", "")
            externals[ref] += 1
        before_c, before_l = len(ex.controls), len(ex.lights)
        ex.run(root, templates, rel(path, pkg))
        file_stats.append((path, len(ex.controls) - before_c, len(ex.lights) - before_l, len(own)))

    controls = ex.controls
    # Update code anywhere that writes a control's state or written vars
    writers = collections.defaultdict(list)
    for cid, writes, code, init in ex.updates:
        for w in writes:
            writers[w].append(cid + (" (init)" if init else ""))
    for row in controls:
        hits, logic = [], False
        for v in uniq([s.split(" ")[0] for s in row["_state"] if s.startswith("L:")] + [w.split(" ")[0] for w in row["_writes"]]):
            comps = uniq(writers.get(v[2:], []))
            if comps:
                hits.append(f"{v} by {', '.join(comps[:3])}" + (" …" if len(comps) > 3 else ""))
                logic = logic or any(not c.endswith("(init)") for c in comps)
        row["updates"] = clip("; ".join(hits), 300)
        if logic:
            row["notes"].append("Update code writes its vars (not only an init)")
    node_count = collections.Counter((r["file"], r["node"]) for r in controls)
    for row in controls:
        if node_count[(row["file"], row["node"])] > 1:
            row["notes"].append("node has several control rows")

    # lights: which control each sits on
    control_names = {}
    for key in ("_state", "_writes"):  # a control's own node and state first, so a var it only writes ranks lower
        for row in controls:
            names = ([row["node"]] if key == "_state" else []) + [v.split(":", 1)[1].split(" ")[0] for v in row[key] if ":" in v]
            for name in names:
                control_names.setdefault(name.upper(), row["node"])
    light_rows = []
    backlight_only = 0
    shared = collections.Counter(v for lt in ex.lights.values() for v in lt["lvars"])
    for (file, node), lt in ex.lights.items():
        if not lt["lvars"] and not lt["others"]:
            backlight_only += 1
            continue
        lt["on"] = sits_on(node, [v for v in lt["lvars"] if shared[v] <= 3], control_names)
        light_rows.append(lt)

    summary = {"backlight_only": backlight_only, "file_stats": file_stats, "used_by": used_by, "roles": roles,
               "bases": bases, "ex": ex, "externals": externals, "defined_local": defined_local, "fallback": fallback,
               "errors": errors, "compiled_interiors": compiled_interiors}
    return controls, light_rows, summary


def main(pkg: pathlib.Path, out: pathlib.Path) -> None:
    out.mkdir(parents=True, exist_ok=True)
    controls, light_rows, s = extract(pkg)
    header = ["file", "line", "panel", "section", "component", "node", "template", "kind", "positions", "state_vars",
              "state_code", "animtips", "tooltip", "tooltip_value", "left_single", "release", "cw", "ccw",
              "other_codes", "writes_lvars", "events", "simvars", "update_writes", "notes"]
    write_tsv(out / "control-map.tsv", header,
              [[r["file"], r["line"], r["panel"], r["section"], r["component"], r["node"], r["template"], r["kind"],
                r["positions"], r["state_vars"], r["state_code"], r["animtips"], r["tooltip"], r["tooltip_value"],
                r["left"], r["release"], r["cw"], r["ccw"], r["other"], r["writes"], r["events"], r["simvars"],
                r["updates"], "; ".join(r["notes"])] for r in controls])
    write_tsv(out / "lights.tsv", ["file", "line", "panel", "section", "component", "node", "template", "lvars",
                                   "circuits", "potentiometers", "other_simvars", "on_control", "code"],
              [[lt["file"], lt["line"], lt["panel"] or "(no banner)", lt["path"], ", ".join(lt["components"][:3]),
                lt["node"], ", ".join(lt["sources"]), ", ".join("L:" + v for v in lt["lvars"]), ", ".join(lt["circuits"]),
                ", ".join(lt["pots"]), clip(", ".join(lt["others"]), 200), lt["on"], clip(" || ".join(lt["code"]), 300)]
               for lt in light_rows])
    write_panels(out / "panels.md", pkg, controls, light_rows)
    write_summary(out / "summary.md", pkg, controls, light_rows, s["backlight_only"], s["file_stats"], s["used_by"],
                  s["roles"], s["bases"], s["ex"], s["externals"], s["defined_local"], s["fallback"], s["errors"],
                  s["compiled_interiors"])
    print((out / "summary.md").read_text(encoding="utf-8").split("\n## ")[0])


def sits_on(node: str, lvars: list, control_names: dict) -> str:
    """The control a light belongs to: its node or one of its own vars with a light suffix (_DECAL, _ON ...) cut off
    names a control; failing that, the one control sharing the longest name prefix (marked '?')."""
    for name in [node] + lvars:
        parts = name.upper().split("_")
        if "_".join(parts) in control_names:
            return control_names["_".join(parts)]
        cut = 0
        while cut < 3 and len(parts) - cut > 1 and parts[len(parts) - cut - 1] in LIGHT_SUFFIXES:
            cut += 1
            cand = "_".join(parts[:len(parts) - cut])
            if cand in control_names:
                return control_names[cand]
    parts = re.sub(r"_(" + "|".join(LIGHT_SUFFIXES) + r")$", "", node.upper()).split("_")
    for n in range(len(parts) - 1, 2, -1):
        cand = "_".join(parts[:n])
        hits = sorted({v for k, v in control_names.items() if k.startswith(cand + "_") or k == cand})
        if len(hits) == 1:
            return hits[0] + "?"
        if hits:
            break
    words = set(re.split(r"[_\-]", "_".join(parts[:-1]))) - {""}  # drop the legend word (PRESS, INOP, OFF ...)
    if len(words) >= 4:
        hits = sorted({v for k, v in control_names.items() if words <= set(re.split(r"[_\-]", k))})
        if len(hits) == 1:
            return hits[0] + "?"
    return ""


def write_panels(path: pathlib.Path, pkg: pathlib.Path, controls: list, lights: list) -> None:
    panels, files = {}, []
    for items, kind in ((controls, "c"), (lights, "l")):
        for r in items:
            key = (r["file"], r["panel"] or "(no banner)")
            data = panels.setdefault(key, {"c": [], "l": [], "path": r.get("section", r.get("path", "")), "line": r["line"]})
            data[kind].append(r)
            data["line"] = min(data["line"], r["line"])
            if r["file"] not in files:
                files.append(r["file"])
    lines = [f"# Panels: {pkg.name}", "",
             "One section per banner comment that labels controls or lights, in file order (a banner the vendor "
             "reused is one section); \"(after X)\" holds what the vendor placed after section X closed and before "
             "the next banner. Kinds, notes and labels as in control-map.tsv; a label that only says "
             "DECREASE/INCREASE or ON/OFF counts as no label. A note every control of a kind shares in a panel is "
             "given once for the kind.", ""]
    last_file = None
    for (file, panel), data in sorted(panels.items(), key=lambda kv: (files.index(kv[0][0]), kv[1]["line"])):
        cs, ls = data["c"], data["l"]
        if file != last_file:
            lines += [f"## File `{file}`", ""]
            last_file = file
        lines.append(f"### {panel}")
        lines.append("")
        if data["path"] and data["path"] != panel:
            lines.append(f"- Section: {data['path']}")
        kinds = collections.Counter(r["kind"] for r in cs)
        lines.append(f"- Controls: {len(cs)}" + (" (" + ", ".join(f"{k} {kinds[k]}" for k in KINDS if kinds[k]) + ")" if cs else ""))
        lines.append(f"- Lights: {len(ls)}")
        irregular = [r for r in cs if any(not n.startswith("node has several") for n in r["notes"])]
        if irregular:
            lines.append(f"- Irregular ({len(irregular)}):")
            shown = 0
            by_kind = collections.OrderedDict()
            for r in irregular:
                by_kind.setdefault(r["kind"], []).append(r)
            for k, rows in by_kind.items():
                keys = {tuple(note_key(n) for n in r["notes"]) for r in rows}
                if len(rows) > 3 and len(keys) == 1 and len(rows) == sum(1 for r in cs if r["kind"] == k):
                    lines.append(f"  - every {k} ({len(rows)}): " + "; ".join(next(iter(keys))))
                    continue
                for r in rows:
                    if shown < 25:
                        lines.append(f"  - `{r['node']}` ({r['kind']}): " + "; ".join(r["notes"]))
                    shown += 1
            if shown > 25:
                lines.append(f"  - … and {shown - 25} more (control-map.tsv, notes column)")
        unlabeled = [r for r in cs if not r["_labeled"]]
        if unlabeled:
            by_kind = collections.OrderedDict()
            for r in unlabeled:
                by_kind.setdefault(r["kind"], []).append(r["node"])
            lines.append(f"- No label, wording must come from the node name ({len(unlabeled)}):")
            for k, nodes in by_kind.items():
                shown = ", ".join(f"`{n}`" for n in nodes[:12])
                more = f" … and {len(nodes) - 12} more" if len(nodes) > 12 else ""
                lines.append(f"  - {k} ({len(nodes)}): {shown}{more}")
        lines.append("")
    path.write_text("\n".join(lines), encoding="utf-8", newline="\n")


def write_summary(path, pkg, controls, lights, backlight_only, file_stats, used_by, roles, bases, ex, externals,
                  defined_local, fallback, errors, compiled_interiors) -> None:
    manifest = {}
    if (pkg / "manifest.json").is_file():
        try:
            manifest = json.loads(read_text(pkg / "manifest.json"))
        except ValueError:
            pass
    kinds = collections.Counter(r["kind"] for r in controls)
    panels, files = {}, []
    for items, slot in ((controls, 0), (lights, 1)):
        for r in items:
            if r["file"] not in files:
                files.append(r["file"])
            entry = panels.setdefault(r["panel"] or "(no banner)", [0, 0, (len(files), 0)])
            entry[slot] += 1
            entry[2] = min(entry[2], (files.index(r["file"]), r["line"]))
    panels = collections.OrderedDict((p, v[:2]) for p, v in sorted(panels.items(), key=lambda kv: kv[1][2]))
    lvars = collections.Counter()
    for r in controls:
        for v in uniq([s.split(" ")[0] for s in r["_state"] + r["_writes"] if s.startswith("L:")]):
            lvars[v[2:]] += 0
    for lt in lights:
        for v in lt["lvars"]:
            lvars[v] += 0
    names = sorted(lvars)
    prefixes = collections.Counter(prefix_of(n) for n in names)
    suffixes = collections.Counter(s for n in names for s in SUFFIXES if n.upper().endswith(s))
    events = collections.Counter(e for r in controls for e in r["events"].split(", ") if e)
    plain = [r for r in controls if r["state_vars"].startswith("L:") and r["writes"] and not any(
        n.startswith(("H:", "K:", "B:", "writes ", "stock", "state not", "bitmask", "click gated", "unresolved"))
        for n in r["notes"])]
    labeled = [r for r in controls if r["_labeled"]]
    notes = collections.Counter(note_key(n) for r in controls for n in r["notes"])
    light_vars = collections.Counter(v for lt in lights for v in lt["lvars"])
    plain_kinds = collections.Counter(r["kind"] for r in plain)
    lines = [
        f"# Control map: {pkg.name}",
        "",
        f"- Title: {manifest.get('title', '?')} · creator: {manifest.get('creator', '?')} · version: "
        f"{manifest.get('package_version', '?')}",
        f"- Interactive controls: {len(controls)}; lights (annunciators and legends reading an L: or a non-lighting A: "
        f"var): {len(lights)}; emissive elements driven only by circuits and potentiometers (backlighting, not listed): "
        f"{backlight_only}",
        "- Controls by kind: " + ", ".join(f"{k} {kinds[k]}" for k in KINDS if kinds[k]),
        f"- Plain L:var controls (state is an L:var, the click writes L:vars, no event, bitmask, gate or stock "
        f"template): {len(plain)}; controls with a label of their own (ANIMTIP or tooltip title beyond "
        f"DECREASE/INCREASE/ON/OFF): {len(labeled)}",
        f"- Panels (banner sections holding controls or lights): {len(panels)}",
        f"- L:vars named by controls and lights: {len(names)}; events fired by clicks: "
        f"{sum(1 for e in events if e.startswith('K:'))} K:, {sum(1 for e in events if e.startswith('H:'))} H:, "
        f"{sum(1 for e in events if e.startswith('B:'))} B:",
        f"- Package templates defined: {len(defined_local)}, expanded {sum(ex.template_uses.values())} times; "
        f"sim templates used as leaves: {len(ex.leaf_uses)}",
    ]
    if fallback:
        lines.append("- No interior named by a model.cfg has source behaviors, so every ModelInfo and ModelBehaviors XML "
                     "under SimObjects was read instead")
    if compiled_interiors:
        lines.append(f"- Compiled behaviors (Behaviors Compiled=\"True\", logic in *.behavior.xml, not mapped here; "
                     f"survey_package.py lists their B: input events): {len(compiled_interiors)} files, e.g. "
                     + ", ".join(f"`{rel(p, pkg)}`" for p in compiled_interiors[:3]))
    if errors:
        lines.append("- Files that did not parse: " + "; ".join(errors))
    lines += ["", "## Interior files and variants", "",
              "| File | Role | Used by (model folders) | Controls | Lights | Own templates |", "|---|---|---|---|---|---|"]
    base_nodes = collections.defaultdict(set)
    for r in controls:
        base_nodes[r["file"]].add(r["node"])
    for p, nc, nl, nt in file_stats:
        users = ", ".join(sorted(used_by.get(p, []))) or ("(base of the interiors below)"
                                                            if roles.get(p) == "interior base" else "-")
        lines.append(f"| `{rel(p, pkg)}` | {roles.get(p, '')} | {users} | {nc} | {nl} | {nt} |")
    for p, nc, nl, nt in file_stats:
        bs = [rel(b, pkg) for b in bases.get(p, [])]
        if bs and nc:
            mine = [r for r in controls if r["file"] == rel(p, pkg)]
            base_all = set().union(*(base_nodes[b] for b in bs))
            new = [r["node"] for r in mine if r["node"] not in base_all]
            lines.append(f"- `{rel(p, pkg)}` adds to its base: " + ", ".join(
                f"`{r['node']}` ({r['kind']}, state {r['state_vars'] or 'none'}, writes {r['writes'] or 'nothing'})"
                for r in mine[:10]) + (f"; {len(new)} of its nodes are not in the base" if new else ""))
    lines += ["", "## Panels", "", "| Panel | Controls | Lights |", "|---|---|---|"]
    lines += [f"| {p} | {c} | {l} |" for p, (c, l) in panels.items()]
    lines += ["", "## Naming conventions", "",
              "L:var prefixes (first two underscore-separated words; unique names among state vars, written vars and "
              "light vars):", ""]
    lines += [f"- `{p}`: {n}" for p, n in prefixes.most_common(40)]
    if len(prefixes) > 40:
        lines.append(f"- … {len(prefixes) - 40} more prefixes")
    lines += ["", "Suffixes:", ""] + [f"- `{s}`: {n}" for s, n in suffixes.most_common()]
    lines += ["", "Light vars read by the most lights (a shared var is a lamp test, a failure or a power gate, not a "
              "legend of its own):", ""]
    lines += [f"- `L:{v}`: {n}" for v, n in light_vars.most_common(8) if n > 1]
    lines += ["", f"Lights with a `_DECAL` var: {sum(1 for lt in lights if any(v.upper().endswith('_DECAL') for v in lt['lvars']))}"
              f"; with a `_BULB_FAILED` var: "
              f"{sum(1 for lt in lights if any(v.upper().endswith('_BULB_FAILED') for v in lt['lvars']))}; gated by "
              f"`A:CIRCUIT ON:n`: {sum(1 for lt in lights if any('CIRCUIT ON' in c.upper() for c in lt['circuits']))}"
              f"; mapped to a control by name: {sum(1 for lt in lights if lt['on'] and not lt['on'].endswith('?'))}"
              f" (and {sum(1 for lt in lights if lt['on'].endswith('?'))} guessed, marked '?')"]
    lines += ["", "Events fired by clicks (count of controls):", ""]
    lines += [f"- `{e}`: {n}" for e, n in sorted(events.items())]
    lines += ["", "## Templates", "", "Package templates expanded (uses): " +
              ", ".join(f"{n} ({c})" for n, c in ex.template_uses.most_common()),
              "", "Package templates never used: " + (", ".join(sorted(defined_local - set(ex.template_uses))) or "none"),
              "", "Sim templates used as leaves (not in the package; uses): " +
              ", ".join(f"{n} ({c})" for n, c in ex.leaf_uses.most_common()),
              "", "Sim behavior libraries included: " + (", ".join(f"`{n}`" for n in sorted(externals)) or "none")]
    if ex.unresolved_templates:
        lines.append("", )
        lines.append("Template names left unresolved: " + ", ".join(ex.unresolved_templates))
    lines += ["", "## Kinds", "",
              "- toggle: a click flips one var (`(L:X) ! (>L:X)` or an equivalent if/els), or a click whose new value "
              "depends on the var it writes, with at most two labels.",
              "- multi-position switch: three or more labels, a `ASOBO_GT_Switch_3States`, or a click that steps a "
              "var between clamps (`1 + 2 min`).",
              "- momentary button: press writes and release resets (a pulse), or a press that only sets a value or "
              "fires an event; held button: the template times the press (`FIRE_HELD_EVENT`, `MIN_HELD_DURATION`, "
              "a `_HELD` var).",
              "- select button (beyond the brief's list): a press sets its own var to 1 and clears its group "
              "(radio-style audio selectors).",
              "- rotary knob infinite / finite: turn codes (or a knob template); infinite when the code wraps (`%`, "
              "`d 35 > if{ 0 }`) or the template is an infinite knob.",
              "- lever: a drag or axis template (`DRAG_CODE`); guard: node or var named GUARD, GAURD (the vendor's "
              "spelling) or COVER; cdu key: the CDU key templates.",
              "- circuit breaker (beyond the brief's list): a click that XORs one bit of a shared mask var.",
              "- other: flight controls and anything the rules above do not match (see the notes column)."]
    lines += ["", "## Irregularities (count of controls)", ""] + [f"- {n}: {c}" for n, c in notes.most_common()]
    lines += ["", "## What a generator can derive, and what needs curation", "",
              f"- Mechanical: panel grouping (banner), node, kind, positions, state var and the RPN to replay for each "
              f"click, for every row; for the {len(plain)} plain L:var controls that is the whole definition (read the "
              f"state var, replay the click code over the calculator path): "
              + ", ".join(f"{k} {plain_kinds[k]}" for k in KINDS if plain_kinds[k]) + ".",
              f"- Mechanical with a rule: circuit breakers ({kinds['circuit breaker']}) share bitmask vars, one bit "
              f"each: the bit is the constant in the click code; momentary and held buttons ({kinds['momentary button']}"
              f" + {kinds['held button']}) need a press, a hold time and a release; CDU keys ({kinds['cdu key']}) "
              f"follow one var per key.",
              f"- Curation: wording for the {len(controls) - len(labeled)} controls with no label of their own (the "
              f"node name is all there is); position names where ANIMTIP describes a direction (knobs) rather than "
              f"positions; controls driven by K:/H:/B: events whose real state is a stock simvar or lives in the "
              f"aircraft's WASM; controls whose state is not an L:var; guarded switches (the guard var must be open "
              f"first); variant-specific controls (see the variant table); lights mapped to controls by name only "
              f"('?' is a guess).",
              "", "## Assumptions about the sim's templates (not in the package)", "",
              "- `ASOBO_GT_Switch_Code`: ANIM_CODE is the position (0..100 per step); LEFT_SINGLE_CODE runs on a left "
              "click; ANIMTIP_n is the tooltip while the control is in position n (the vendor's text may name the state "
              "or the action). Without any click code it is an animated part (an inscription), not a control.",
              "- `ASOBO_GT_Push_Button_Held_Simple`: LEFT_SINGLE_CODE on press, LEFT_LEAVE_CODE on release (or when "
              "the mouse leaves).",
              "- `ASOBO_GT_Knob_Infinite` / `ASOBO_GT_Knob_Finite_Code`: CLOCKWISE_CODE and ANTICLOCKWISE_CODE per "
              "detent; ANIMTIP_0 is the anticlockwise (decrease) hint and ANIMTIP_1 the clockwise one; VALUE is the "
              "tooltip's value code (it often names where the real value lives).",
              "- `ASOBO_GT_Switch_3States`: the template itself writes SWITCH_POSITION_VAR (of SWITCH_POSITION_TYPE, L "
              "here) to 0, 1 or 2 and then runs CODE_POS_n; STATEn_TEST reads the position back.",
              "- `ASOBO_GT_MouseRect`: CALLBACKCODE runs on every event in MOUSEFLAGS; branches test (M:Event) "
              "'LeftSingle', 'LeftRelease', 'WheelUp' ...; code outside a branch runs on every listed event.",
              "- `ASOBO_GT_Lever_DraggingWheelLeftDrag_Axis`: DRAG_CODE runs while dragging with (M:DragPercent); "
              "POSITIVE_AXIS_CODE and NEGATIVE_AXIS_CODE run on wheel or axis steps.",
              "- `ASOBO_INSTRUMENT_Base_Template` (the vendor's tooltip and CDU-key wrappers): it builds an input "
              "event named IE_NAME whose interaction runs the inherited CLOCKWISE/ANTICLOCKWISE/LEFT_SINGLE codes.",
              "- `ASOBO_GT_Helper_Suffix_ID_Appender`: calls TEMPLATE_TO_CALL; when that is a package template it is "
              "expanded directly (the ID suffixing is not modeled).",
              "- Template logic: <Parameters> without Type and <DefaultTemplateParameters> set a value only when it "
              "is not already set; <Condition Check> means defined, <Condition Valid> means defined and not False/0/"
              "empty; a UseTemplate's children and every enclosing value are visible inside the template.",
              "- High-level stock templates (" + ", ".join(sorted(STOCK_STATE)) + ") keep their logic and state in "
              "the sim; the state var shown for them is the stock simvar such a template is assumed to drive.",
              ]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


if __name__ == "__main__":
    sys.stdout.reconfigure(errors="replace")
    sys.setrecursionlimit(10000)
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    package = pathlib.Path(sys.argv[1]).resolve()
    if not (package / "layout.json").is_file() and not (package / "manifest.json").is_file():
        sys.exit(f"{package} has no manifest.json or layout.json: pass the package folder itself")
    main(package, pathlib.Path(sys.argv[2]).resolve())
