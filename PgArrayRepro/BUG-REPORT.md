# Bug report draft — PropertyGrid array/List<T> not rendered

Target: https://github.com/microsoft/microsoft-ui-reactor/issues
Verified against `upstream/main` @ `de5c1351` (2026-05-30). The fork's `perf-lab`
branch has a zero diff vs upstream for the PropertyGrid directory, so this is an
upstream bug, not a fork regression.

Run `PgArrayRepro` (this folder) to reproduce; screenshot at `../pg-array-bug.png`.

---

**Title:** `PropertyGrid` does not render array/`List<T>` properties — array-editing UI is implemented but never wired into `PropertyGridComponent`

### Summary

A `PropertyGrid` bound to an object with an array or `List<T>` property shows the
property's `ToString()` text (e.g. `System.Collections.Generic.List`1[System.String]`)
instead of an editable list. None of the array UI — count badge, add `[+]`, per-item
rows with move-up / move-down / remove — ever appears.

The supporting infrastructure for array editing exists and is unit-tested, but it is
never invoked by the rendering pipeline, so the feature is effectively dead code.

### Repro

```csharp
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;

class Model
{
    public List<string> Tags { get; set; } = new() { "a", "b" };
}

// inside a Component.Render():
var registry = new TypeRegistry();
return PropertyGrid(new Model(), registry);
```

### Expected

The `Tags` row shows the array editor: a `Tags (2)` header with an Add `[+]` button,
and one row per item with `[▲] [▼] [✕]` controls (i.e. what
`PropertyGridDefaults.ArrayToolbarTemplate` / `ArrayItemTemplate` describe).

### Actual

The `Tags` row shows the literal text `System.Collections.Generic.List`1[System.String]`.
`List<ComplexType>` and `T[]` behave the same way. Nothing is clickable/editable.

### Root cause

In `src/Reactor/Controls/PropertyGrid/PropertyGridComponent.cs`, `RenderProperty`
resolves array/list types to `ArrayTypeMetadata` (via `TypeRegistry.TryResolveArray`),
which has `Editor == null` and `Decompose == null`. As a result:

- `hasEditor` (PropertyGridComponent.cs:151) → `false`
- `hasDecompose` (PropertyGridComponent.cs:150) → `false`

so the value falls through to the scalar fallback `TextBlock(value?.ToString())`
(PropertyGridComponent.cs:161) and is emitted as a plain row (line 236). There is no
branch that detects `ArrayTypeMetadata` and renders the toolbar/item list.

The building blocks are all present but unreferenced by the component:

- `ArrayOperations` (Add / RemoveAt / MoveUp / MoveDown) — referenced **only by its
  own file** across `src/`; the component never calls it.
- `PropertyGridDefaults.ArrayToolbarTemplate` / `ArrayItemTemplate` — defined, never used.
- `ArrayTypeMetadata.CreateElement` — populated by `TryResolveArray`, never consumed.
- `PropertyGridElement.ArrayItemTemplate` / `ArrayToolbarTemplate` override hooks —
  declared, never read.

### Tests / docs mismatch

- `tests/Reactor.Tests/PropertyGridArrayTests.cs` only exercises the static
  `ArrayOperations` helpers directly; there is no test asserting that the rendered grid
  produces array UI, which is why the gap isn't caught.
- `docs/specs/tasks/property-grid-tasks.md` marks all 17 "Phase 5: Array Support" items
  (including *5.4 Array Item Editing — expand array item … recursive PropertyGrid
  rendering*) as complete, which doesn't match the current render path.

### Version

`microsoft/microsoft-ui-reactor` @ `de5c1351` (main, 2026-05-30).
TFM `net10.0-windows10.0.22621.0`, Windows App SDK 2.0.1.

### Suggested fix

Add an `ArrayTypeMetadata` branch in `RenderProperty` that renders
`ArrayToolbarTemplate` + one `ArrayItemTemplate` per item, wiring the buttons to the
existing `ArrayOperations` (in-place mutate for `IList`, replace-via-setter for `T[]`)
and `CreateElement` for Add, then forcing a re-render. The default templates and
operations already exist, so this is primarily a wiring change.
