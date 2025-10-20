# NUnitLegacyAssertUpgrader

A small Roslyn-powered console tool that migrates legacy **NUnit classic asserts** to modern **constraint-style** (`Assert.That(...)`) across your C# test code. Includes quality-of-life features for safe adoption in legacy repos.

---

## Contents

- [What it does](#what-it-does)
- [Converted APIs](#converted-apis)
- [Requirements](#requirements)
- [Build](#build)
- [Quick start](#quick-start)
- [Command-line options](#command-line-options)
- [Examples](#examples)
- [JSON report format](#json-report-format)
- [How it works](#how-it-works)
- [Safety notes](#safety-notes)
- [Limitations](#limitations)
- [Troubleshooting](#troubleshooting)
- [CI usage](#ci-usage)
- [License](#license)

---

## What it does

- Scans `.cs` files and rewrites NUnit **classic** assertions into **constraint-style**.
- Preserves custom messages, including `string.Format` style parameter lists.
- Supports exception assertions and common string/collection helpers.
- Parallelized; can re-run automatically until no more changes are detected.
- Can generate a machine-readable JSON report.

---

## Converted APIs

### Booleans / Null / Empty

| From | To |
|---|---|
| `Assert.IsNull(x)` | `Assert.That(x, Is.Null)` |
| `Assert.IsNotNull(x)` | `Assert.That(x, Is.Not.Null)` |
| `Assert.IsTrue(cond)` | `Assert.That(cond, Is.True)` |
| `Assert.IsFalse(cond)` | `Assert.That(cond, Is.False)` |
| `Assert.IsEmpty(x)` | `Assert.That(x, Is.Empty)` |
| `Assert.IsNotEmpty(x)` | `Assert.That(x, Is.Not.Empty)` |

### Equality / Identity / Ordering

| From | To |
|---|---|
| `Assert.AreEqual(exp, act)` | `Assert.That(act, Is.EqualTo(exp))` |
| `Assert.AreEqual(exp, act, delta)` | `Assert.That(act, Is.EqualTo(exp).Within(delta))` |
| `Assert.AreNotEqual(exp, act)` | `Assert.That(act, Is.Not.EqualTo(exp))` |
| `Assert.AreNotEqual(exp, act, delta)` | `Assert.That(act, Is.Not.EqualTo(exp).Within(delta))` |
| `Assert.AreSame(exp, act)` | `Assert.That(act, Is.SameAs(exp))` |
| `Assert.AreNotSame(exp, act)` | `Assert.That(act, Is.Not.SameAs(exp))` |
| `Assert.Greater(a, b)` | `Assert.That(a, Is.GreaterThan(b))` |
| `Assert.GreaterOrEqual(a, b)` | `Assert.That(a, Is.GreaterThanOrEqualTo(b))` |
| `Assert.Less(a, b)` | `Assert.That(a, Is.LessThan(b))` |
| `Assert.LessOrEqual(a, b)` | `Assert.That(a, Is.LessThanOrEqualTo(b))` |

### Strings

| From | To |
|---|---|
| `StringAssert.Contains(sub, actual)` | `Assert.That(actual, Does.Contain(sub))` |
| `StringAssert.StartsWith(sub, actual)` | `Assert.That(actual, Does.StartWith(sub))` |
| `StringAssert.EndsWith(sub, actual)` | `Assert.That(actual, Does.EndWith(sub))` |
| `StringAssert.Matches(pattern, actual)` / `IsMatch` | `Assert.That(actual, Does.Match(pattern))` |
| `StringAssert.DoesNotMatch(pattern, actual)` / `IsNotMatch` | `Assert.That(actual, Does.Not.Match(pattern))` |

### Collections

| From | To |
|---|---|
| `CollectionAssert.Contains(coll, item)` | `Assert.That(coll, Does.Contain(item))` |
| `CollectionAssert.DoesNotContain(coll, item)` | `Assert.That(coll, Does.Not.Contain(item))` |
| `CollectionAssert.AreEquivalent(exp, act)` | `Assert.That(act, Is.EquivalentTo(exp))` |

### Exceptions

| From | To |
|---|---|
| `Assert.Throws<T>(() => ...)` | `Assert.That(() => ..., Throws.TypeOf<T>())` |
| `Assert.Throws(typeof(T), () => ...)` | `Assert.That(() => ..., Throws.TypeOf<T>())` |
| `Assert.ThrowsAsync<T>(async () => ...)` | `Assert.That(async () => ..., Throws.TypeOf<T>())` |
| `Assert.Catch<T>(() => ...)` | `Assert.That(() => ..., Throws.TypeOf<T>())`* |
| `Assert.DoesNotThrow(() => ...)` | `Assert.That(() => ..., Throws.Nothing)` |
| `Assert.DoesNotThrowAsync(async () => ...)` | `Assert.That(async () => ..., Throws.Nothing)` |

\* If you rely on `Catch<T>` accepting subtypes, change the mapping to `Throws.InstanceOf<T>()` for that case.

**Messages**  

- Trailing messages like `Assert.AreEqual(x, y, "oops")` are preserved as the third argument:  
  `Assert.That(y, Is.EqualTo(x), "oops")`.
- Formatted messages like `Assert.AreEqual(x, y, "oops {0}", id)` become:  
  `Assert.That(y, Is.EqualTo(x), string.Format("oops {0}", id))`.

---

## Requirements

- [.NET SDK 8.0+](https://dotnet.microsoft.com/)
- (Optional) `git` on PATH for `--since-git`
- NUnit assertions present in your codebase

---

## Build

```bash
dotnet build -c Release
```

The executable is produced at:

```bash
./bin/Release/net8.0/NUnitAssertUpgrader
```

---

## Quick start

Preview changes without modifying files:

```bash
./bin/Release/net8.0/NUnitAssertUpgrader "<repo-root>" --dry-run --stats
```

Apply changes to all `.cs` files under `<repo-root>`:

```bash
./bin/Release/net8.0/NUnitAssertUpgrader "<repo-root>" --write-until-clean --stats
```

Generate a JSON report:

```bash
./bin/Release/net8.0/NUnitAssertUpgrader "<repo-root>" --write-until-clean --report refactor-report.json
```

---

## Command-line options

```

NUnitAssertUpgrader <repo-root> [options]

Options:
  --dry-run              Preview only (no writes)
  --backup               Create .bak files for changed sources
  --since-git            Only process files changed/untracked according to git
  --include <globs>      Comma- or space-separated globs (default: **/*.cs)
  --exclude <globs>      Comma- or space-separated globs (default: **/bin/**,**/obj/**)
  --workers <n>          Max degree of parallelism (default: CPU count)
  --stats                Print per-rule conversion counts

  --write-until-clean    Re-run passes until no further changes (max 5 passes)
  --report <path.json>   Emit a JSON report (processed, changed, file list, rule stats)

  --help                 Show usage
```

Notes:

- `--write-until-clean` is ignored in `--dry-run` mode.
- Globs support `**`, `*`, `?`. Multiple globs can be separated by commas or spaces.

---

## Examples

Only process test files changed since your current Git state:

```bash
./NUnitAssertUpgrader . --since-git --include "**/*.Tests.cs" --stats
```

Backup originals and exclude generated code:

```bash
./NUnitAssertUpgrader ./src --backup --exclude "**/Generated/**,**/obj/**" --write-until-clean
```

Run with 8 workers and produce a report:

```bash
./NUnitAssertUpgrader . --workers 8 --write-until-clean --report upgrade-report.json
```

---

## JSON report format

`--report <path.json>` writes a file like:

```json
{
  "Processed": 412,
  "Changed": 73,
  "DurationSeconds": 12.7,
  "FilesChanged": [
    "/abs/path/Tests/FooTests.cs",
    "/abs/path/Tests/BarTests.cs"
  ],
  "RuleStats": {
    "Assert.AreEqual → That(Is.EqualTo)": 216,
    "StringAssert.Contains → That(Does.Contain)": 18,
    "Assert.Throws → That(Throws.TypeOf)": 9
  }
}
```

- **Processed**: number of file visits across passes.
- **Changed**: number of unique files written.
- **FilesChanged**: absolute paths, sorted.
- **RuleStats**: counts per transformation rule.

---

## How it works

- Parses each C# file into a Roslyn syntax tree.
- Finds invocation expressions on `Assert`, `StringAssert`, `CollectionAssert`.
- Rewrites known classic patterns to constraint-style equivalents.
- Reconstructs the file while preserving trivia (formatting/comments).
- Optional repeated passes handle chained rewrites.

---

## Safety notes

- Use source control. Commit before running.
- `--backup` creates `*.bak` alongside changed files.
- Review with `git diff` before committing.
- Run your test suite after conversion.

---

## Limitations

- Only rewrites supported classic APIs listed above.
- Exotic overloads or custom wrappers may not be rewritten.
- `Catch<T>` is mapped to `Throws.TypeOf<T>()` by default (strict). If you expect subtype acceptance, replace with `Throws.InstanceOf<T>()`.
- Does not adjust `using` directives; not usually required for NUnit constraint-style.

---

## Troubleshooting

**Tool finds nothing**  

- Verify the `<repo-root>` path.
- Check `--include`/`--exclude` globs.
- Try without `--since-git`.

**Unexpected formatting of messages**  

- The tool converts `message + params` into `string.Format(message, params)`. If you use custom format providers, adjust manually.

**Compilation errors after conversion**  

- Ensure NUnit constraint namespaces are referenced (`NUnit.Framework`).
- Check for custom assertion wrappers that need manual updates.

---

## CI usage

Add a job that runs the upgrader and fails if changes are detected (enforcing migration):

```bash
./NUnitAssertUpgrader . --write-until-clean
git diff --quiet || { echo "Assertion migration produced changes. Commit them."; exit 1; }
```

Or just produce a report artifact:

```bash
./NUnitAssertUpgrader . --since-git --write-until-clean --report nunit-migration.json
```

---

## License

Internal tooling for legacy migration. Runs under the [MIT LICENSE](LICENSE.txt)