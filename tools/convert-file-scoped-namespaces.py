"""Convert block-scoped namespaces to file-scoped (C# 10)."""

from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "src"
NAMESPACE_LINE = re.compile(r"^namespace\s+([\w.]+)\s*(?:\{)?\s*$")
USING_LINE = re.compile(r"^\s*using\s+")


def unindent(text: str) -> str:
    lines = text.splitlines(keepends=True)
    out: list[str] = []
    for line in lines:
        if line.startswith("    "):
            out.append(line[4:])
        elif line.startswith("\t"):
            out.append(line[1:])
        else:
            out.append(line)
    return "".join(out)


def convert(source: str) -> str | None:
    if re.search(r"^namespace\s+[\w.]+\s*;", source, re.M):
        return None

    namespace_matches = list(re.finditer(r"^namespace\s+", source, re.M))
    if len(namespace_matches) != 1:
        return None

    lines = source.splitlines(keepends=True)
    ns_index = None
    ns_name = None
    brace_on_same_line = False
    for i, line in enumerate(lines):
        stripped = line.strip()
        m = NAMESPACE_LINE.match(stripped)
        if m:
            ns_index = i
            ns_name = m.group(1)
            brace_on_same_line = stripped.endswith("{") and not stripped.endswith(";")
            break
    if ns_index is None or ns_name is None:
        return None

    # Find opening brace
    open_index = ns_index if brace_on_same_line else None
    if open_index is None:
        for i in range(ns_index + 1, len(lines)):
            if lines[i].strip() == "{":
                open_index = i
                break
            if lines[i].strip() and not lines[i].strip().startswith("//"):
                return None
    if open_index is None:
        return None

    depth = 0
    close_index = None
    start_char_scan = open_index
    for i in range(start_char_scan, len(lines)):
        # Count braces outside strings roughly via strip of comments
        for ch in lines[i]:
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    close_index = i
                    break
        if close_index is not None:
            break
    if close_index is None:
        return None

    header = "".join(lines[:ns_index])
    inner = "".join(lines[open_index + 1 : close_index])
    trailing = "".join(lines[close_index + 1 :])

    inner_unindented = unindent(inner)
    inner_lines = inner_unindented.splitlines(keepends=True)

    moved_usings: list[str] = []
    body_lines: list[str] = []
    still_usings = True
    for line in inner_lines:
        if still_usings:
            if line.strip() == "" or USING_LINE.match(line) or line.strip().startswith("//"):
                if USING_LINE.match(line):
                    moved_usings.append(line.lstrip())
                elif line.strip().startswith("//") and not body_lines:
                    moved_usings.append(line.lstrip() if line.startswith(" ") else line)
                else:
                    # blank between usings
                    if moved_usings:
                        moved_usings.append(line if not line.startswith(" ") else line.lstrip() or "\n")
                    else:
                        body_lines.append(line)
                continue
            still_usings = False
        body_lines.append(line)

    # Keep usings that were already in the header
    parts: list[str] = []
    if header:
        parts.append(header.rstrip() + "\n")
    if moved_usings:
        using_block = "".join(moved_usings).strip("\n") + "\n"
        if parts:
            parts.append("\n")
        parts.append(using_block)
    if parts:
        parts.append("\n")
    parts.append(f"namespace {ns_name};\n\n")
    body = "".join(body_lines).strip("\n")
    if body:
        parts.append(body + "\n")
    if trailing.strip():
        parts.append(trailing)
    result = "".join(parts)
    if not result.endswith("\n"):
        result += "\n"
    return result


def main() -> None:
    converted = 0
    skipped = 0
    for path in ROOT.rglob("*.cs"):
        if any(part in {"bin", "obj"} for part in path.parts):
            continue
        original = path.read_text(encoding="utf-8-sig")
        updated = convert(original)
        if updated is None or updated == original:
            skipped += 1
            continue
        path.write_text(updated, encoding="utf-8", newline="\n")
        converted += 1
        print(f"converted {path.relative_to(ROOT)}")
    print(f"done: {converted} converted, {skipped} skipped")


if __name__ == "__main__":
    main()
