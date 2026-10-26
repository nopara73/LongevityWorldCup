"""Partition discovered tests, then require every method on exactly one runner.

Coverage is merged by source line and branch identity, never by averaging rates.
Only timings are cached; a stale cache cannot change the discovered test set.
"""
import argparse
from collections import Counter, defaultdict
import copy
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET


def plan_tests(names, timings, count, revision):
    cases = Counter(name.strip() for name in names if name.strip())
    if not cases or count < 1:
        raise ValueError("No tests discovered or invalid shard count")
    for name in cases:
        if not re.fullmatch(r"[\w.+`]+", name):
            raise ValueError(f"Unsupported fully qualified test name: {name}")
    weights = {}
    for name, discovered in cases.items():
        previous = timings.get(name, {})
        if not isinstance(previous, dict):
            previous = {}
        seconds = previous.get("seconds", discovered)
        if not isinstance(seconds, (float, int)) or not 0 < seconds < 86400:
            seconds = discovered
        weights[name] = max(.001, seconds)
    shards = [dict(index=i, methods=[], estimatedSeconds=0) for i in range(count)]
    for name in sorted(cases, key=lambda name: (-weights[name], name)):
        shard = min(shards, key=lambda shard: (shard["estimatedSeconds"], shard["index"]))
        shard["methods"].append(name)
        shard["estimatedSeconds"] += weights[name]
    if any(not shard["methods"] for shard in shards):
        raise ValueError("An empty shard would hide a discovery/planning error")
    return dict(revision=revision, shards=shards, discoveredMethods=len(cases))


def write_plan(plan, directory):
    directory.mkdir(parents=True, exist_ok=True)
    (directory / "plan.json").write_text(json.dumps(plan, indent=2) + "\n")
    for shard in plan["shards"]:
        settings = ET.Element("RunSettings")
        configuration = ET.SubElement(settings, "RunConfiguration")
        ET.SubElement(configuration, "TestCaseFilter").text = "|".join(
            "FullyQualifiedName=" + name for name in shard["methods"])
        ET.ElementTree(settings).write(directory / f"shard-{shard['index']}.runsettings",
                                       encoding="utf-8", xml_declaration=True)


def seconds(value):
    hours, minutes, rest = value.split(":")
    return int(hours) * 3600 + int(minutes) * 60 + float(rest)


def read_results(path):
    root = ET.parse(path).getroot()
    definitions = {}
    for test in root.findall(".//{*}UnitTest"):
        method = test.find("{*}TestMethod")
        definitions[test.attrib["id"]] = method.attrib["className"].split(",")[0] + "." + method.attrib["name"]
    results = []
    for result in root.findall(".//{*}UnitTestResult"):
        results.append(dict(method=definitions[result.attrib["testId"]],
                            id=result.attrib["testId"],
                            executionId=result.attrib["executionId"],
                            name=result.attrib["testName"], outcome=result.attrib["outcome"],
                            seconds=seconds(result.attrib.get("duration", "0:0:0"))))
    if not results:
        raise ValueError(f"No executed tests in {path}")
    return results


def verify_results(plan, reports):
    planned = [name for shard in plan["shards"] for name in shard["methods"]]
    if len(planned) != len(set(planned)) or len(planned) != plan["discoveredMethods"]:
        raise ValueError("The plan omitted a method or assigned it to multiple runners")
    if set(reports) != {shard["index"] for shard in plan["shards"]}:
        raise ValueError("Missing or unexpected shard reports")
    identities = set()
    timings = defaultdict(lambda: dict(seconds=0, cases=0))
    for shard in plan["shards"]:
        results = reports[shard["index"]]
        actual = {result["method"] for result in results}
        if actual != set(shard["methods"]):
            raise ValueError(f"Shard {shard['index']} did not execute its exact planned methods: "
                             f"missing={set(shard['methods']) - actual}; extra={actual - set(shard['methods'])}")
        for result in results:
            # Deferred data rows can share both testId and a truncated name.
            # TRX executionId identifies the actual execution record; the
            # disjoint method plan prevents overlap between runners.
            identity = result["executionId"]
            if identity in identities:
                raise ValueError(f"Duplicate test execution: {identity}")
            identities.add(identity)
            if result["outcome"] != "Passed":
                raise ValueError(f"Test failed, skipped or aborted: {result['name']} ({result['outcome']})")
            timing = timings[result["method"]]
            timing["seconds"] += max(.001, result["seconds"])
            timing["cases"] += 1
    return len(identities), dict(timings)


def merge_coverage(reports):
    merged = {}
    for report in reports:
        if not report:
            raise ValueError("Empty coverage report")
        for module, documents in report.items():
            for document, classes in documents.items():
                for class_name, methods in classes.items():
                    target = merged.setdefault(module, {}).setdefault(document, {}).setdefault(class_name, {})
                    for method, observed in methods.items():
                        if method not in target:
                            target[method] = copy.deepcopy(observed)
                            continue
                        combined = target[method]
                        for line, hits in observed["Lines"].items():
                            combined["Lines"][line] = combined["Lines"].get(line, 0) + hits
                        branches = {branch_key(branch): branch for branch in combined["Branches"]}
                        for branch in observed["Branches"]:
                            key = branch_key(branch)
                            if key in branches:
                                branches[key]["Hits"] += branch["Hits"]
                            else:
                                branches[key] = copy.deepcopy(branch)
                        combined["Branches"] = list(branches.values())
    return merged


def branch_key(branch):
    return tuple(branch[key] for key in ("Line", "Offset", "EndOffset", "Path", "Ordinal"))


def coverage_totals(coverage):
    lines, branches = [], []
    for documents in coverage.values():
        for classes in documents.values():
            for methods in classes.values():
                for method in methods.values():
                    lines.extend(method["Lines"].values())
                    branches.extend(branch["Hits"] for branch in method["Branches"])
    if not lines:
        raise ValueError("Coverage contains no source lines")
    return dict(linesValid=len(lines), linesCovered=sum(hits > 0 for hits in lines),
                branchesValid=len(branches), branchesCovered=sum(hits > 0 for hits in branches))


def aggregate(plan, directory, output):
    reports, coverage = {}, []
    for shard in plan["shards"]:
        folder = directory / f"test-results-{shard['index']}"
        reports[shard["index"]] = read_results(folder / "tests.trx")
        coverage.append(json.loads((folder / "coverage.json").read_text()))
    total, timings = verify_results(plan, reports)
    merged = merge_coverage(coverage)
    totals = coverage_totals(merged)
    output.mkdir(parents=True, exist_ok=True)
    (output / "coverage.json").write_text(json.dumps(merged))
    (output / "timings.json").write_text(json.dumps(timings))
    (output / "verification.json").write_text(json.dumps(dict(
        revision=plan["revision"], passed=total, methods=plan["discoveredMethods"],
        shards=len(reports), coverage=totals), indent=2) + "\n")
    summary = ("## .NET test coverage\n\n"
               f"- Tests: **{total:,} passed**, across {len(reports)} isolated runners\n"
               f"- All {plan['discoveredMethods']:,} discovered methods executed in their assigned shard\n"
               f"- Line coverage: {totals['linesCovered'] / totals['linesValid']:.1%}\n"
               f"- Branch coverage: {totals['branchesCovered'] / max(1, totals['branchesValid']):.1%}\n")
    (output / "summary.md").write_text(summary)
    print(summary)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    partition = commands.add_parser("plan")
    partition.add_argument("--discovery", type=Path, required=True)
    partition.add_argument("--timings", type=Path)
    partition.add_argument("--output", type=Path, required=True)
    partition.add_argument("--revision", required=True)
    partition.add_argument("--shards", type=int, default=4)
    report = commands.add_parser("aggregate")
    report.add_argument("--plan", type=Path, required=True)
    report.add_argument("--results", type=Path, required=True)
    report.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "plan":
        timings = {}
        if args.timings and args.timings.exists():
            try:
                timings = json.loads(args.timings.read_text())
                if not isinstance(timings, dict):
                    timings = {}
            except (OSError, ValueError):
                print("Ignoring unreadable optional timing cache; all discovered tests remain included")
        plan = plan_tests(args.discovery.read_text(encoding="utf-8-sig").splitlines(),
                          timings, args.shards, args.revision)
        write_plan(plan, args.output)
        print(f"Assigned {plan['discoveredMethods']} discovered methods to {len(plan['shards'])} shards")
        measured = sum(name in timings for shard in plan["shards"] for name in shard["methods"])
        print(f"Measured durations available for {measured} methods")
    else:
        aggregate(json.loads(args.plan.read_text()), args.results, args.output)


if __name__ == "__main__":
    main()
