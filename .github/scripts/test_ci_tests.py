import copy
import unittest

from ci_tests import coverage_totals, merge_coverage, plan_tests, verify_results


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


if __name__ == "__main__":
    unittest.main()
