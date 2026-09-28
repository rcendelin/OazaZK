#!/usr/bin/env python3
"""Coverage gate for the calculation logic (X6).

Merges every coverage.cobertura.xml under a directory (one per test project;
the same source file can be exercised from several), keyed by source file and
line, and checks line coverage of the files matched by the globs in
coverage-gate.txt. Fails (exit 1) when their combined line coverage is below
the threshold. Writes a Markdown table to $GITHUB_STEP_SUMMARY when set.

Usage: coverage_gate.py <results-dir> [--config coverage-gate.txt] [--threshold 90]
"""
import argparse
import fnmatch
import os
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict


def load_hits(results_dir):
    """(normalized source path) -> {line number: max hits}."""
    hits = defaultdict(dict)
    for root, _, files in os.walk(results_dir):
        for name in files:
            if name != "coverage.cobertura.xml":
                continue
            tree = ET.parse(os.path.join(root, name))
            sources = [s.text or "" for s in tree.iter("source")]
            for cls in tree.iter("class"):
                filename = cls.get("filename", "")
                path = filename
                if not os.path.isabs(path) and sources:
                    path = os.path.join(sources[0], filename)
                path = normalize(path)
                for line in cls.iter("line"):
                    number = int(line.get("number"))
                    count = int(line.get("hits", "0"))
                    hits[path][number] = max(hits[path].get(number, 0), count)
    return hits


def normalize(path):
    path = path.replace("\\", "/")
    marker = "/api/src/"
    index = path.find(marker)
    return path[index + len("/api/"):] if index >= 0 else path


def load_globs(config):
    with open(config, encoding="utf-8") as handle:
        return [line.strip() for line in handle if line.strip() and not line.startswith("#")]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("results_dir")
    parser.add_argument("--config", default=os.path.join(os.path.dirname(__file__), "..", "coverage-gate.txt"))
    parser.add_argument("--threshold", type=float, default=90.0)
    args = parser.parse_args()

    hits = load_hits(args.results_dir)
    globs = load_globs(args.config)
    gated = sorted(p for p in hits if any(fnmatch.fnmatch(p, g) for g in globs))
    unmatched = [g for g in globs if not any(fnmatch.fnmatch(p, g) for p in hits)]

    rows, covered_total, lines_total = [], 0, 0
    for path in gated:
        lines = hits[path]
        covered = sum(1 for count in lines.values() if count > 0)
        covered_total += covered
        lines_total += len(lines)
        rows.append((path, covered, len(lines)))

    combined = 100.0 * covered_total / lines_total if lines_total else 0.0
    ok = lines_total > 0 and combined >= args.threshold

    report = ["## Pokrytí výpočetní logiky", "",
              f"**{combined:.1f} %** řádků ({covered_total}/{lines_total}), hranice {args.threshold:.0f} % — "
              + ("OK" if ok else "POD HRANICÍ"), "",
              "| Soubor | Pokryto | Řádků | % |", "|---|---:|---:|---:|"]
    for path, covered, total in rows:
        report.append(f"| `{path}` | {covered} | {total} | {100.0 * covered / total:.1f} |")
    if unmatched:
        report += ["", "Vzory bez shody (soubor neexistuje nebo nemá testy): " + ", ".join(f"`{g}`" for g in unmatched)]
    text = "\n".join(report)
    print(text)

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write(text + "\n")

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
