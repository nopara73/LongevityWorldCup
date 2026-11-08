"""Reject new unexplained browser-test sleeps; leave existing waits grandfathered.

This is a small lexical guard, not a C#/JavaScript parser. See README.md#tests.
Only Git trees are compared; it does not inspect uncommitted working-tree edits.
"""
import argparse
from collections import Counter
from dataclasses import dataclass
import difflib
import json
import os
from pathlib import Path
import re
import subprocess
import sys


@dataclass
class Literal:
    start: int
    end: int
    text: str
    positions: list


@dataclass
class Wait:
    line: int
    end_line: int
    signature: str
    explained: bool


CS_LITERAL = re.compile(r'[$@]*("{3,}|"|\')')
JS_LITERAL = re.compile(r'["\'`]')


def lex(source, csharp=True):
    """Mask comments/literals without moving offsets; retain inline JS candidates."""
    masked = list(source)
    comments, literals = [], []
    i = 0
    while i < len(source):
        start = i
        if source.startswith('//', i):
            i = source.find('\n', i)
            if i < 0:
                i = len(source)
            standalone = not source[source.rfind('\n', 0, start) + 1:start].strip()
            comments.append((start, source[start:i], standalone))
        elif source.startswith('/*', i):
            end = source.find('*/', i + 2)
            i = len(source) if end < 0 else end + 2
        else:
            opening = (CS_LITERAL if csharp else JS_LITERAL).match(source, i)
            if not opening:
                i += 1
                continue
            prefix = opening.group()
            quote = opening.group(1) if csharp else prefix
            i += len(prefix)
            text, positions = [], []
            if len(quote) >= 3:
                end = source.find(quote, i)
                end = len(source) if end < 0 else end
                text, positions = list(source[i:end]), list(range(i, end))
                i = min(len(source), end + len(quote))
            else:
                while i < len(source):
                    if source[i] == quote:
                        if '@' in prefix and source[i:i + 2] == quote * 2:
                            text.append(quote)
                            positions.append(i)
                            i += 2
                            continue
                        i += 1
                        break
                    if source[i] == '\\' and '@' not in prefix and i + 1 < len(source):
                        text.append({'n': '\n', 'r': '\r', 't': '\t'}.get(source[i + 1], source[i + 1]))
                        positions.append(i)
                        i += 2
                    else:
                        text.append(source[i])
                        positions.append(i)
                        i += 1
            literals.append(Literal(start, i, ''.join(text), positions))
        masked[start:i] = ['\n' if char == '\n' else ' ' for char in source[start:i]]
    return ''.join(masked), comments, literals


def call_end(code, opening):
    depth = 0
    for i in range(opening, len(code)):
        if code[i] == '(':
            depth += 1
        elif code[i] == ')':
            depth -= 1
            if depth == 0:
                return i + 1
    return len(code)


def arguments(code):
    depth, start = 0, 0
    result = []
    for i, char in enumerate(code):
        if char in '([{':
            depth += 1
        elif char in ')]}':
            depth -= 1
        elif char == ',' and depth == 0:
            result.append(code[start:i].strip())
            start = i + 1
    return result + [code[start:].strip()]


def first_argument_end(code, start, end):
    depth = 0
    for i in range(start, end):
        if code[i] in '([{':
            depth += 1
        elif code[i] in ')]}':
            depth -= 1
        elif code[i] == ',' and depth == 0:
            return i
    return end


CS_WAIT = re.compile(r'(?:(?:\b\w+\s*\??)?\.\s*(?P<page>WaitForTimeoutAsync)|\bTask\s*\.\s*Delay|\bThread\s*\.\s*Sleep)\s*\(')
JS_API = re.compile(r'\b(?:EvaluateAsync|EvaluateAllAsync|WaitForFunctionAsync|AddInitScriptAsync|AddScriptTagAsync)(?:\s*<(?:[^<>]|<[^<>]*>)*>)?\s*\(')
PROMISE = re.compile(r'\bnew\s+Promise\s*\(\s*(?:function\s*)?\(?\s*(\w+)')
TIMEOUT = re.compile(r'\bsetTimeout\s*\(')
REASON = re.compile(r'^//\s*Intentional wait:\s*\S.*$', re.IGNORECASE)


def signature(source, csharp=True):
    code, _, literals = lex(source, csharp)
    return re.sub(r'\s+', '', code) + json.dumps([literal.text for literal in literals])


def find_waits(source):
    code, comments, literals = lex(source)
    found = []
    for match in CS_WAIT.finditer(code):
        end = call_end(code, match.end() - 1)
        signature_start = match.start('page') if match.group('page') else match.start()
        found.append((match.start(), end, signature(source[signature_start:end])))
    scripts = [(m.end(), first_argument_end(code, m.end(), call_end(code, m.end() - 1)))
               for m in JS_API.finditer(code)]
    for literal in literals:
        if not any(start <= literal.start < end for start, end in scripts):
            continue
        js, js_comments, _ = lex(literal.text, csharp=False)
        comments.extend((literal.positions[start], text, standalone) for start, text, standalone in js_comments)
        for promise in PROMISE.finditer(js):
            end = call_end(js, js.index('(', promise.start()))
            for timer in TIMEOUT.finditer(js, promise.end(), end):
                timer_end = call_end(js, timer.end() - 1)
                args = arguments(js[timer.end():timer_end - 1])
                resolve = re.escape(promise.group(1))
                # A deferred task with zero delay is a queue checkpoint, not a timed sleep.
                if len(args) < 2 or re.fullmatch(r'0+(?:\.0+)?', args[1]):
                    continue
                callback = args[0]
                if not (re.fullmatch(resolve, callback) or
                        re.match(r'(?:\(\s*\)\s*=>\s*\{?\s*|function\s*\(\s*\)\s*\{\s*)' + resolve + r'\s*\(', callback)):
                    continue
                start_pos = literal.positions[promise.start()]
                end_pos = literal.positions[end - 1] + 1
                found.append((start_pos, end_pos, signature(literal.text[timer.start():timer_end], csharp=False)))
    found = sorted(set(found))
    explained = set()
    for position, text, standalone in comments:
        if not REASON.match(text):
            continue
        line = source.count('\n', 0, position) + 1
        if standalone:
            # One standalone note belongs to the following wait, never the whole method.
            eligible = [start for start, _, _ in found if start > position
                        and source.count('\n', 0, start) + 1 in (line, line + 1)]
            if eligible:
                explained.add(min(eligible))
        else:
            # A trailing note belongs only to its own call, not the next line's wait.
            eligible = [(end, start) for start, end, _ in found if end <= position
                        and source.count('\n', 0, end - 1) + 1 == line]
            if eligible:
                explained.add(max(eligible)[1])
    waits = []
    for start, end, fingerprint in found:
        first = source.count('\n', 0, start) + 1
        last = source.count('\n', 0, end - 1) + 1
        waits.append(Wait(first, last, fingerprint, start in explained))
    return waits


def browser_source(path, source):
    if not path.startswith('LongevityWorldCup.Tests/') or not path.endswith('.cs'):
        return False
    code, _, _ = lex(source)
    return 'Browser' in Path(path).name or bool(re.search(r'\bMicrosoft\s*\.\s*Playwright\b|\bBrowserIntegrationTest\b', code))


def violations(before, after):
    # Exact legacy calls can move or sit beside an edited assertion. New copies,
    # changed arguments and removed explanations still need their own reason.
    legacy = Counter(wait.signature for wait in find_waits(before) if not wait.explained)
    unchanged = set()
    for block in difflib.SequenceMatcher(None, before.splitlines(), after.splitlines(), autojunk=False).get_matching_blocks():
        unchanged.update(range(block.b + 1, block.b + block.size + 1))
    # Give unchanged occurrences their allowance first, so a newly inserted
    # duplicate is reported at its new location rather than an untouched call.
    waits = sorted(find_waits(after), key=lambda wait: not set(range(wait.line, wait.end_line + 1)) <= unchanged)
    failures = []
    for wait in waits:
        if wait.explained:
            continue
        if legacy[wait.signature]:
            legacy[wait.signature] -= 1
        else:
            failures.append(wait.line)
    return sorted(failures)


def git(*args, cwd=None, input=None, check=True):
    result = subprocess.run(['git', *args], cwd=cwd, input=input, text=True,
                            encoding='utf-8', stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if check and result.returncode:
        raise ValueError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout if check else result


def parents(head, cwd=None):
    # Raw headers retain parent IDs even when HEAD is the shallow boundary.
    return re.findall(r'^parent ([0-9a-f]+)$', git('cat-file', 'commit', head, cwd=cwd).split('\n\n', 1)[0], re.MULTILINE)


def comparison_base(event_name, event, head, cwd=None):
    if event_name == 'push' and event.get('before', '').strip('0'):
        before = event['before']
        if not re.fullmatch(r'[0-9a-f]{40,64}', before):
            raise ValueError('Push before must be a full Git commit ID.')
        return before, 'complete push batch'
    previous = parents(head, cwd)
    if event_name == 'pull_request' and len(previous) < 2:
        raise ValueError('Expected the default pull-request merge checkout; use --base for a custom checkout.')
    if previous:
        return previous[0], 'tested merge base' if event_name == 'pull_request' else 'first-parent fallback (no push range)'
    return git('hash-object', '-w', '-t', 'tree', '--stdin', input='', cwd=cwd).strip(), 'initial commit (empty tree)'


def ensure_base(base, cwd=None):
    if git('cat-file', '-e', base, cwd=cwd, check=False).returncode:
        git('fetch', '--no-tags', '--depth=1', 'origin', base, cwd=cwd)


def check_range(base, head, cwd=None):
    fields = git('diff', '--name-status', '--find-renames', '-z', base, head, '--',
                 'LongevityWorldCup.Tests', cwd=cwd).split('\0')
    failures, checked = [], 0
    index = 0
    while index < len(fields) - 1:
        status, old_path = fields[index:index + 2]
        index += 2
        path = old_path
        if status.startswith(('R', 'C')):
            path = fields[index]
            index += 1
        if status == 'D' or not path.endswith('.cs'):
            continue
        after = git('show', f'{head}:{path}', cwd=cwd)
        before = '' if status == 'A' else git('show', f'{base}:{old_path}', cwd=cwd)
        if not (browser_source(path, after) or browser_source(old_path, before)):
            continue
        checked += 1
        failures.extend((path, line) for line in violations(before, after))
    return checked, failures


def annotation(value):
    return value.replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A').replace(',', '%2C').replace(':', '%3A')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', help='Explicit comparison commit; otherwise use the GitHub event or first parent.')
    parser.add_argument('--head', default='HEAD')
    parser.add_argument('--event-name', default=os.environ.get('GITHUB_EVENT_NAME', ''))
    parser.add_argument('--event-path', default=os.environ.get('GITHUB_EVENT_PATH'))
    args = parser.parse_args()
    try:
        head = git('rev-parse', '--verify', args.head + '^{commit}').strip()
        event = json.loads(Path(args.event_path).read_text(encoding='utf-8')) if args.event_path else {}
        base, mode = (args.base, 'explicit base') if args.base else comparison_base(args.event_name, event, head)
        ensure_base(base)
        print(f'Browser wait guard: {base} -> {head} ({mode})')
        checked, failures = check_range(base, head)
        for path, line in failures:
            message = ('New or edited browser-test sleep: wait for a locator/state/request instead, or add '
                       'an adjacent // Intentional wait: <why elapsed time is part of this test>. See README.md#tests.')
            print(f'{path}:{line}: {message}')
            if os.environ.get('GITHUB_ACTIONS') == 'true':
                print(f'::error file={annotation(path)},line={line}::{annotation(message)}')
        print(f'Checked {checked} changed browser-test files; {len(failures)} unexplained waits.')
        return 1 if failures else 0
    except (ValueError, OSError) as error:
        print(f'Browser wait guard failed: {error}', file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
