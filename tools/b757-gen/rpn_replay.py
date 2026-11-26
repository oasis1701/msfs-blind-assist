"""
A small RPN interpreter for the click code of the BlueBird 757's cockpit, so that a test can replay it offline.

bbs757_control_map.json holds the cockpit's own click code (press, release, cw, ccw) verbatim, and the app replays it
through the sim's calculator. replay() runs the same code on a copy of a dict of L:vars and returns the vars afterwards,
which is how a test learns where a click leaves a control (test_generate_b757_map.py, ClickCodeReplay). Only the subset
the cockpit's code uses is understood; anything else is a ReplayError, never a guess.

    numbers       12  -1.5
    variables     (L:NAME)  (L:NAME, unit)  (>L:NAME, unit)    the unit is ignored
                  (A:NAME, unit)  (E:NAME, unit)  (O:NAME)     read as 0 until given
                  (>O:NAME)                                    recorded
                  (>K:NAME)  (>H:NAME)                         recorded, and nothing else happens
    operators     !  +  -  ++  --  min  max  ==  <  >  d  %  and  or
    blocks        if{ ... }   if{ ... } els{ ... }

Operands come off the stack in RPN order: "a b -" is a - b and "a b >" is 1 when a > b. A comparison, "!", "and" and "or"
push 1 or 0; "and" and "or" take any non-zero value as true. "d" copies the top of the stack. A set pops the top of the
stack into the variable, and if{ takes its condition the same way. A pop from an empty stack gives 0: the cockpit's own
code needs that (46 of the map's press codes pop an empty stack, counted with the vendor slips' stray token left out as
test_generate_b757_map.py replays them; the bus tie and the APU generator buttons are among them, writing a var and then
running a "!" with nothing left to negate). The sim's own behavior there has not been measured.

The returned dict holds the L:vars under their bare names, as map.state_var gives them. Every other variable lives in it
under its type and name, "O:LOCK" or "K:AUTOPILOT_OFF": an O:var written earlier in the code reads back, an A: or E: var
reads 0 unless it is given under that key, and a K: or H: event is recorded with the value it was sent with, so none of
them can be taken for an L:var. The dict passed in is never changed.
"""
import math
import re


class ReplayError(ValueError):
    """The code is not RPN this interpreter understands, or cannot run."""


# One token: a parenthesized variable, a block opener, a closing brace, or a word (a number or an operator).
_TOKEN = re.compile(r"\s*(?:(?P<var>\([^()]*\))|(?P<open>if\{|els\{)|(?P<close>\})|(?P<word>[^\s(){}]+))")
_NUMBER = re.compile(r"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?\Z")

_TYPES = "LAEOKH"    # the variable types the cockpit's code uses
_READABLE = "LAEO"
_WRITABLE = "LOKH"


def _pop(stack):
    return stack.pop() if stack else 0.0


def _unary(function):
    def run(stack):
        stack.append(float(function(_pop(stack))))
    return run


def _binary(function):
    def run(stack):
        b = _pop(stack)
        a = _pop(stack)
        stack.append(float(function(a, b)))
    return run


def _modulo(a, b):
    if b == 0:
        raise ReplayError("modulo by zero")
    return math.fmod(a, b)


def _dup(stack):
    top = _pop(stack)
    stack.extend((top, top))


_OPERATORS = {
    "!": _unary(lambda a: a == 0),
    "++": _unary(lambda a: a + 1),
    "--": _unary(lambda a: a - 1),
    "+": _binary(lambda a, b: a + b),
    "-": _binary(lambda a, b: a - b),
    "min": _binary(min),
    "max": _binary(max),
    "==": _binary(lambda a, b: a == b),
    "<": _binary(lambda a, b: a < b),
    ">": _binary(lambda a, b: a > b),
    "and": _binary(lambda a, b: a != 0 and b != 0),
    "or": _binary(lambda a, b: a != 0 or b != 0),
    "%": _binary(_modulo),
    "d": _dup,
}


def _tokens(code):
    position = 0
    while True:
        match = _TOKEN.match(code, position)
        if match is None:
            rest = code[position:].strip()
            if not rest:
                return
            if rest.startswith("("):
                raise ReplayError(f"an opening parenthesis that is never closed: {rest[:40]!r}")
            raise ReplayError(f"unexpected {rest[0]!r} in {rest[:40]!r}")
        position = match.end()
        yield match.lastgroup, match.group(match.lastgroup)


def _variable(text):
    """("get" | "set", key) from "(L:NAME, unit)" or "(>L:NAME, unit)": the bare name for an L:var, else "TYPE:NAME"."""
    body = text[1:-1].strip()
    write = body.startswith(">")
    scope, colon, rest = body[1:].lstrip().partition(":") if write else body.partition(":")
    if not colon or not re.fullmatch(r"[A-Za-z]", scope):
        raise ReplayError(f"a variable with no type: {text}")
    if scope not in _TYPES:
        raise ReplayError(f"unsupported variable type {scope!r}: {text}")
    if scope not in (_WRITABLE if write else _READABLE):
        raise ReplayError(f"{scope}: variables cannot be {'written' if write else 'read'}: {text}")
    name = rest.rsplit(",", 1)[0].strip()    # what follows the last comma is the unit
    if not name:
        raise ReplayError(f"a variable with no name: {text}")
    return ("set" if write else "get", name if scope == "L" else f"{scope}:{name}")


def _word(text):
    if _NUMBER.match(text):
        return ("num", float(text))
    if text in _OPERATORS:
        return ("op", text)
    raise ReplayError(f"unknown token {text!r}")


def _parse(code):
    """The code as nested items: ("num", value), ("op", name), ("get", key), ("set", key), ("if", then, else-or-None)."""
    tokens = list(_tokens(code))
    position = 0

    def block(inside):
        nonlocal position
        items = []
        while position < len(tokens):
            kind, text = tokens[position]
            position += 1
            if kind == "close":
                if not inside:
                    raise ReplayError("a closing brace nothing opened")
                return items
            if kind == "open":
                if text == "els{":
                    raise ReplayError("els{ with no if{ before it")
                then, other = block(True), None
                if position < len(tokens) and tokens[position] == ("open", "els{"):
                    position += 1
                    other = block(True)
                items.append(("if", then, other))
            elif kind == "var":
                items.append(_variable(text))
            else:
                items.append(_word(text))
        if inside:
            raise ReplayError("a block that is never closed")
        return items

    return block(False)


def _run(items, state, stack):
    for item in items:
        kind = item[0]
        if kind == "num":
            stack.append(item[1])
        elif kind == "get":
            stack.append(float(state.get(item[1], 0.0)))
        elif kind == "set":
            state[item[1]] = _pop(stack)
        elif kind == "op":
            _OPERATORS[item[1]](stack)
        else:
            _, then, other = item
            branch = then if _pop(stack) != 0 else other
            if branch:
                _run(branch, state, stack)


def replay(code: str, lvars: dict[str, float]) -> dict[str, float]:
    """Run click code on a copy of lvars and return the variables afterwards (see the module's docstring)."""
    program = _parse(code)
    state = dict(lvars)
    _run(program, state, [])
    return state
