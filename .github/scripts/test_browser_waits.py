import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from check_browser_waits import browser_source, check_range, comparison_base, ensure_base, find_waits, git, parents, violations


class TestWaitPolicy(unittest.TestCase):
    def test_csharp_calls_and_changed_multiline_arguments(self):
        for call in ['await page.WaitForTimeoutAsync(100);', 'await Task.Delay(100);',
                     'await GetPage().WaitForTimeoutAsync(100);',
                     'System.Threading.Thread.Sleep(100);',
                     'await page\n .WaitForTimeoutAsync(\n 100\n);',
                     'await Task\n .Delay(TimeSpan.FromSeconds(\n 1\n));']:
            with self.subTest(call=call):
                self.assertEqual(1, len(violations('', call)))
                self.assertEqual([], violations(call, call + '\nAssert.True(ready);'))
                self.assertEqual(1, len(violations(call, call.replace('100', '200').replace(' 1\n', ' 2\n'))))

    def test_literals_and_comments_are_not_calls(self):
        source = r'''
            // await Task.Delay(100);
            /* page.WaitForTimeoutAsync(100); */
            var text = "Thread.Sleep(100)";
            var verbatim = @"Task.Delay(100) and ""quoted"" text";
            var raw = """page.WaitForTimeoutAsync(100)""";
            var example = "new Promise(resolve => setTimeout(resolve, 100))";
            await page.EvaluateAsync("() => 'new Promise(r => setTimeout(r, 100))'");
            await page.EvaluateAsync("value => value", "new Promise(r => setTimeout(r, 100))");
            await page.EvaluateAsync("""
                () => {
                    // await new Promise(resolve => setTimeout(resolve, 100));
                    const label = `new Promise(r => setTimeout(r, 100))`;
                    return label;
                }
                """);
            '''
        self.assertEqual([], find_waits(source))

    def test_inline_javascript_sleeps_in_supported_literal_forms(self):
        scripts = [
            'await page.EvaluateAsync("() => new Promise(r => setTimeout(r, 100))");',
            'await page.EvaluateAsync<bool>(@"async () => { await new Promise(r => window.setTimeout(r, delay)); return true; }");',
            'await page.EvaluateAsync<List<double>>("async () => { await new Promise(r => setTimeout(r, 100)); return []; }");',
            '''await context.AddInitScriptAsync("""
                async () => { await new Promise((resolve) => {
                    setTimeout(resolve, 100);
                }); }
                """);''',
            'await page.EvaluateAsync("() => new Promise(r => setTimeout(() => r(false), 100))");',
            'await page.EvaluateAsync("() => new Promise(function(resolve) { setTimeout(function() { resolve(); }, 100); })");',
        ]
        for source in scripts:
            with self.subTest(source=source):
                self.assertEqual(1, len(violations('', source)))

    def test_condition_signal_clock_and_queue_waits_pass(self):
        source = '''
            await Expect(page.Locator("#ready")).ToBeVisibleAsync();
            await page.WaitForFunctionAsync("() => window.ready");
            await page.WaitForResponseAsync("**/api/events");
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await page.Clock.RunForAsync(1000);
            await page.Clock.FastForwardAsync(1000);
            await page.EvaluateAsync("() => new Promise(r => setTimeout(r, 0))");
            await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(r))");
            await page.EvaluateAsync("setTimeout(() => location.reload(), 0)");
            '''
        self.assertEqual([], violations('', source))

    def test_each_deliberate_wait_needs_its_own_local_reason(self):
        explained = '// Intentional wait: Observe the old completion deadline.\nawait Task.Delay(500);'
        self.assertEqual([], violations('', explained))
        self.assertEqual([], violations('', explained.replace('Task.Delay(500)', 'Task\n .Delay(\n 500\n)')))
        trailing = 'await Task.Delay(500); // Intentional wait: Let the stale callback attempt to reopen.\n'
        self.assertEqual([], violations('', trailing))
        self.assertEqual([2], violations('', trailing + 'await Task.Delay(600);'))
        self.assertEqual([2], violations('', '// Intentional wait:\nawait Task.Delay(500);'))
        self.assertEqual([3], violations('', '// Intentional wait: Observe stale callbacks.\nAssert.True(ready);\nawait Task.Delay(500);'))
        self.assertEqual([2], violations('', 'var note = "// Intentional wait: Fake approval";\nawait Task.Delay(500);'))
        self.assertEqual([1], violations('', 'await Task.Delay(100); ' + trailing))
        self.assertEqual([1], violations(explained, 'await Task.Delay(500);'))

    def test_javascript_reason_is_local_to_the_promise(self):
        source = '''await page.EvaluateAsync("""
            async () => {
                // Intentional wait: Observe the retry deadline while the response is held.
                await new Promise(resolve =>
                    setTimeout(resolve, 500));
                await new Promise(resolve => setTimeout(resolve, 600));
            }
            """);'''
        self.assertEqual([6], violations('', source))
        escaped = r'await page.EvaluateAsync("async () => {\n// Intentional wait: Observe stale callbacks.\nawait new Promise(r => setTimeout(r, 500)); }");'
        self.assertEqual([], violations('', escaped))

    def test_unchanged_wait_beside_changed_assertion_passes(self):
        before = 'void Old() {\n await Task.Delay(100);\n Assert.True(before);\n}\n'
        self.assertEqual([], violations(before, before.replace('True(before)', 'True(after)')))
        self.assertEqual([], violations(before, before.replace('Delay(100)', 'Delay( 100 )')))
        self.assertEqual([2], violations(before, before.replace('100', '200')))
        after = 'void New() {\n await Task.Delay(100);\n}\n\n' + before
        self.assertEqual([2], violations(before, after))
        before = 'await page.WaitForTimeoutAsync(100);'
        self.assertEqual([], violations(before, before.replace('page.', 'mainPage.')))

    def test_changed_wait_cannot_borrow_its_removed_reason(self):
        before = '// Intentional wait: Outlive the grace period.\nawait Task.Delay(100);'
        self.assertEqual([1], violations(before, 'await Task.Delay(200);'))
        before = 'await Task.Delay(TimeSpan.Parse("00:00:01"));'
        self.assertEqual([1], violations(before, before.replace('00:00:01', '00:00:02')))

    def test_browser_scope_includes_playwright_sources_and_helpers_only(self):
        for name in ['HomepageVisitLayoutTests.cs', 'LabAccessPanelTests.cs', 'WebPushWorkerLifecycleTests.cs']:
            self.assertTrue(browser_source('LongevityWorldCup.Tests/' + name, 'using Microsoft.Playwright;'))
        self.assertTrue(browser_source('LongevityWorldCup.Tests/SomeBrowserTests.Partial.cs', ''))
        self.assertTrue(browser_source('LongevityWorldCup.Tests/Fixture.cs', 'class Fixture : BrowserIntegrationTest {}'))
        self.assertFalse(browser_source('LongevityWorldCup.Tests/DatabaseFileWatcherTests.cs', '// Microsoft.Playwright'))
        self.assertFalse(browser_source('LongevityWorldCup.Tests/SiteStatisticsDashboardPageTests.cs', 'using Xunit;'))
        self.assertFalse(browser_source('LongevityWorldCup.Website/Browser.cs', 'using Microsoft.Playwright;'))


class TestGitComparisons(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='lwc-wait-guard-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / 'repo'
        self.root.mkdir()
        self.run_git('init', '-b', 'master')
        self.run_git('config', 'user.name', 'Guard test')
        self.run_git('config', 'user.email', 'guard@example.test')
        self.path = 'LongevityWorldCup.Tests/ExampleBrowserTests.cs'
        self.write(self.path, 'await Task.Delay(10);\nAssert.True(before);\n')
        self.base = self.commit()

    def run_git(self, *args):
        return git(*args, cwd=self.root).strip()

    def write(self, path, text):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding='utf-8')

    def commit(self):
        self.run_git('add', '.')
        self.run_git('commit', '-m', 'fixture')
        return self.run_git('rev-parse', 'HEAD')

    def shallow_clone(self):
        clone = Path(self.temporary.name) / 'shallow'
        git('clone', '--depth=1', self.root.as_uri(), str(clone))
        return clone

    def test_whole_push_batch_in_shallow_checkout_and_actionable_cli(self):
        self.write(self.path, 'await Task.Delay(10);\nawait Task.Delay(20);\n')
        self.commit()
        self.write('LongevityWorldCup.Tests/WebPushWorkerLifecycleTests.cs',
                   'using Microsoft.Playwright;\nawait page.EvaluateAsync("() => new Promise(r => setTimeout(r, 100))");')
        self.write('LongevityWorldCup.Tests/BackendTests.cs', 'await Task.Delay(500);')
        head = self.commit()
        clone = self.shallow_clone()
        base, mode = comparison_base('push', {'before': self.base}, head, cwd=clone)
        self.assertEqual('complete push batch', mode)
        ensure_base(base, cwd=clone)
        checked, failures = check_range(base, head, cwd=clone)
        self.assertEqual(2, checked)
        self.assertEqual([(self.path, 2), ('LongevityWorldCup.Tests/WebPushWorkerLifecycleTests.cs', 2)], failures)
        self.assertEqual('true', git('rev-parse', '--is-shallow-repository', cwd=clone).strip())
        event = clone / 'event.json'
        event.write_text(json.dumps({'before': self.base}), encoding='utf-8')
        script = Path(__file__).with_name('check_browser_waits.py').resolve()
        result = subprocess.run([sys.executable, '-B', str(script), '--event-name', 'push', '--event-path', str(event)],
                                cwd=clone, capture_output=True, text=True,
                                env={**os.environ, 'GITHUB_ACTIONS': 'true'})
        self.assertEqual(1, result.returncode, result.stderr)
        self.assertIn('::error file=' + self.path + ',line=2::', result.stdout)

    def test_pr_uses_tested_merge_parent_even_with_empty_or_stale_payload(self):
        self.run_git('checkout', '-b', 'feature')
        feature = 'LongevityWorldCup.Tests/FeatureBrowserTests.cs'
        self.write(feature, 'await Task.Delay(500);')
        self.commit()
        self.run_git('checkout', 'master')
        self.write('LongevityWorldCup.Tests/BaseBrowserTests.cs', 'await Task.Delay(600);')
        actual_base = self.commit()
        self.run_git('merge', '--no-ff', 'feature', '-m', 'merge fixture')
        head = self.run_git('rev-parse', 'HEAD')
        clone = self.shallow_clone()
        self.assertEqual(actual_base, parents(head, cwd=clone)[0])
        for event in [{}, {'pull_request': {'base': {'sha': self.base}}}]:
            base, _ = comparison_base('pull_request', event, head, cwd=clone)
            self.assertEqual(actual_base, base)
            ensure_base(base, cwd=clone)
            self.assertEqual([(feature, 1)], check_range(base, head, cwd=clone)[1])

    def test_manual_zero_before_and_initial_commit_have_explicit_fallbacks(self):
        empty, mode = comparison_base('workflow_dispatch', {}, self.base, cwd=self.root)
        self.assertIn('initial commit', mode)
        self.assertEqual([(self.path, 1)], check_range(empty, self.base, cwd=self.root)[1])
        self.write(self.path, 'await Task.Delay(10);\nawait Task.Delay(20);')
        head = self.commit()
        for event_name, event in [('workflow_dispatch', {}), ('push', {'before': '0' * 40})]:
            base, mode = comparison_base(event_name, event, head, cwd=self.root)
            self.assertEqual(self.base, base)
            self.assertIn('fallback', mode)
        with self.assertRaisesRegex(ValueError, 'merge checkout'):
            comparison_base('pull_request', {}, head, cwd=self.root)

    def test_renamed_legacy_wait_and_ordinary_assertion_change_pass(self):
        renamed = 'LongevityWorldCup.Tests/RenamedBrowserTests.cs'
        self.run_git('mv', self.path, renamed)
        self.write(renamed, 'await Task.Delay(10);\nAssert.True(after);\n')
        head = self.commit()
        self.assertEqual([], check_range(self.base, head, cwd=self.root)[1])


if __name__ == '__main__':
    unittest.main()
