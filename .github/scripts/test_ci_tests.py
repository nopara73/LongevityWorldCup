import copy
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
import xml.etree.ElementTree as ET

from ci_tests import aggregate, coverage_totals, extract_seed_plan, merge_coverage, plan_tests, verify_results


class TestCiIntegrity(unittest.TestCase):
    def setUp(self):
        self.names = [f"Example.Tests.Flow.Case{i}" for i in range(8)]
        self.plan = plan_tests(self.names, {}, 4, "revision")
        self.reports = {shard["index"]: [dict(method=name, name=name, executionId="exec-" + name, outcome="Passed", seconds=1)
                        for name in shard["methods"]] for shard in self.plan["shards"]}

    def test_partition_preserves_new_tests_and_ignores_stale_cached_names(self):
        plan = plan_tests(self.names + [self.names[0]],
                          {"Deleted.Tests.Case": dict(seconds=9999), self.names[0]: dict(seconds=20)}, 4, "revision")
        assigned = [name for shard in plan["shards"] for name in shard["methods"]]
        self.assertCountEqual(self.names, assigned)
        self.assertEqual(8, len(set(assigned)))

    def test_complete_run_passes(self):
        count, timings = verify_results(self.plan, self.reports)
        self.assertEqual(8, count)
        self.assertEqual(set(self.names), set(timings))

    def test_missing_shard_fails(self):
        del self.reports[0]
        with self.assertRaisesRegex(ValueError, "shard reports"):
            verify_results(self.plan, self.reports)

    def test_omitted_method_fails(self):
        self.reports[0].pop()
        with self.assertRaisesRegex(ValueError, "exact planned methods"):
            verify_results(self.plan, self.reports)

    def test_duplicate_case_fails(self):
        self.reports[0].append(copy.deepcopy(self.reports[0][0]))
        with self.assertRaisesRegex(ValueError, "Duplicate test"):
            verify_results(self.plan, self.reports)

    def test_deferred_theory_rows_can_share_a_test_id(self):
        row = self.reports[0][0]
        row.update(id="deferred-theory", name="first data row")
        second = dict(row, name="second data row", executionId="second-execution")
        self.reports[0].append(second)
        count, _ = verify_results(self.plan, self.reports)
        self.assertEqual(9, count)

    def test_truncated_names_can_repeat_with_distinct_test_ids(self):
        row = self.reports[0][0]
        row.update(id="first-case", name="long data row...")
        self.reports[0].append(dict(row, id="second-case", executionId="second-execution"))
        count, _ = verify_results(self.plan, self.reports)
        self.assertEqual(9, count)

    def test_deferred_rows_can_share_truncated_names_and_test_ids(self):
        row = self.reports[0][0]
        row.update(id="deferred-theory", name="data: [...]")
        self.reports[0].append(dict(row, executionId="second-execution"))
        count, _ = verify_results(self.plan, self.reports)
        self.assertEqual(9, count)

    def test_method_assigned_to_two_runners_fails(self):
        self.plan["shards"][1]["methods"].append(self.plan["shards"][0]["methods"][0])
        with self.assertRaisesRegex(ValueError, "multiple runners"):
            verify_results(self.plan, self.reports)

    def test_failed_and_skipped_tests_fail_the_aggregate(self):
        for outcome in ["Failed", "NotExecuted", "Aborted"]:
            with self.subTest(outcome=outcome):
                self.reports[0][0]["outcome"] = outcome
                with self.assertRaisesRegex(ValueError, "failed, skipped or aborted"):
                    verify_results(self.plan, self.reports)

    def test_complementary_branches_merge_to_full_coverage(self):
        def report(hits):
            branches = [dict(Line=1, Offset=2, EndOffset=4, Path=i, Ordinal=i, Hits=hit)
                        for i, hit in enumerate(hits)]
            return {"App.dll": {"App.cs": {"App": {"Run()": {"Lines": {"1": 1, "2": 0}, "Branches": branches}}}}}
        first, second = report([1, 0]), report([0, 1])
        merged = merge_coverage([first, second])
        self.assertEqual(dict(linesValid=2, linesCovered=1, branchesValid=2, branchesCovered=2),
                         coverage_totals(merged))
        self.assertEqual(0, first["App.dll"]["App.cs"]["App"]["Run()"]["Branches"][1]["Hits"])

    def test_empty_discovery_and_coverage_fail(self):
        with self.assertRaises(ValueError):
            plan_tests([], {}, 4, "revision")
        with self.assertRaises(ValueError):
            coverage_totals({})


class TestRerunArtifacts(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.plan = plan_tests(["Example.Tests.Flow.First", "Example.Tests.Flow.Second"], {}, 2, "revision")

    def artifact(self, shard, attempt=None, outcome="Passed", coverage=True):
        suffix = "" if attempt is None else f"-attempt-{attempt}"
        folder = self.root / f"test-results-{shard}{suffix}"
        folder.mkdir()
        name = self.plan["shards"][shard]["methods"][0]
        class_name, method = name.rsplit(".", 1)
        root = ET.Element("TestRun")
        definition = ET.SubElement(ET.SubElement(root, "TestDefinitions"), "UnitTest", id=name)
        ET.SubElement(definition, "TestMethod", className=class_name, name=method)
        ET.SubElement(ET.SubElement(root, "Results"), "UnitTestResult", testId=name,
                      executionId=name, testName=name, outcome=outcome, duration="0:0:1")
        ET.ElementTree(root).write(folder / "tests.trx", encoding="utf-8")
        if coverage:
            report = {"App.dll": {"App.cs": {"App": {method: {"Lines": {"1": 1}, "Branches": []}}}}}
            (folder / "coverage.json").write_text(json.dumps(report))
        return folder

    def test_partial_retry_uses_new_results_and_retains_other_successful_shards(self):
        # Original failure artifact has only TRX, just like run40428985002.
        old = self.artifact(0, outcome="Failed", coverage=False)
        self.artifact(0, 2, outcome="Failed", coverage=False)
        self.artifact(0, 10)
        self.artifact(1, 1)
        output = self.root / "combined"
        aggregate(self.plan, self.root, output)
        verification = json.loads((output / "verification.json").read_text())
        self.assertEqual((2, 2, 2), (verification["passed"], verification["methods"], verification["shards"]))
        self.assertTrue((old / "tests.trx").exists())

    def test_failed_latest_retry_cannot_fall_back_to_older_passing_results(self):
        self.artifact(0, 1)
        self.artifact(0, 2, outcome="Failed")
        self.artifact(1, 1)
        with self.assertRaisesRegex(ValueError, "failed, skipped or aborted"):
            aggregate(self.plan, self.root, self.root / "combined")
        self.assertFalse((self.root / "combined/verification.json").exists())

    def test_incomplete_latest_retry_cannot_borrow_older_coverage(self):
        self.artifact(0, 1)
        self.artifact(0, 2, coverage=False)
        self.artifact(1, 1)
        with self.assertRaises(FileNotFoundError) as failure:
            aggregate(self.plan, self.root, self.root / "combined")
        self.assertEqual(self.root / "test-results-0-attempt-2/coverage.json", Path(failure.exception.filename))
        self.assertFalse((self.root / "combined/verification.json").exists())

    def test_timing_seed_reads_the_latest_build_plan_without_extracting_other_files(self):
        for name, revision in [("ci-build", "legacy"), ("ci-build-attempt-2", "older"),
                               ("ci-build-attempt-10", "latest")]:
            folder = self.root / name
            folder.mkdir()
            with tarfile.open(folder / "ci-build.tgz", "w:gz") as archive:
                data = json.dumps(dict(self.plan, revision=revision)).encode()
                member = tarfile.TarInfo(".artifacts/ci-tests/plan.json")
                member.size = len(data)
                archive.addfile(member, io.BytesIO(data))
                archive.addfile(tarfile.TarInfo("../unexpected"))
        output = self.root / "seed/plan.json"
        extract_seed_plan(self.root, output)
        self.assertEqual("latest", json.loads(output.read_text())["revision"])
        self.assertFalse((self.root.parent / "unexpected").exists())


if __name__ == "__main__":
    unittest.main()
