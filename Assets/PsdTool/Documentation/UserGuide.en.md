# PSD to Unity uGUI — User guide

[简体中文](使用说明.md) | [English](UserGuide.en.md)

Version: `2026.10.30-preview.11`. Tested with Windows, Unity 2022.3.62f3c1, uGUI 1.0.0 and TextMeshPro 3.0.7. The editor UI is primarily Chinese; translations below explain the existing labels rather than promise an English interface.

## Team responsibilities

Best suited to PSD/uGUI teams with many screens and frequent revisions. The workflow reduces repetitive handoff, assembly and revision work; no measured time-saving percentage is claimed.

- **Artists:** deliver sensible layer groups, independent parts, editable text and font information. Ordinary names, including Chinese, are supported. No Unity helper-node construction is required.
- **Engineers:** parse the PSD, review proposed types/ownership/groups, generate and validate the controls, then implement business logic. Automation reduces positioning and reference wiring, but does not replace acceptance testing.
- **Revisions:** reuse the same source tree and output location, preview changes and update incrementally. Use property locks for manual decisions and check scripts/events afterward.

Visual review, local issue handling, configurable resources and MIT-licensed source support customization. AI and incremental updates are not unique to this project; complex designs still need manual corrections.

## First-time setup

1. Install uGUI and TextMeshPro in your Unity project. Import **Window → TextMeshPro → Import TMP Essential Resources**.
2. Import the released `.unitypackage`, or copy the repository's `Assets/PsdTool` folder and matching `.meta` files into your project. Do not copy project-specific settings or generated data from another team member.
3. Open **Tools → PSD 工具 → 打开工具** (Open tool). The first run creates `Assets/PsdTool/PsdToolSettings.asset` for this project.
4. Set design resolution, resource/prefab output paths and TMP font mappings. Supply licensed fonts with the glyphs required by your artwork.
5. For AI, install the official Codex CLI and authenticate it through the provided first-use prompts. You need working network access and available account allowance. A browser-only ChatGPT login is insufficient. The tool does not require Ollama, a local vision model or a separate API key configuration.
6. For image FX baking, configure a usable local Photoshop executable. Photoshop and fonts are not included. Baking uses a temporary document; without Photoshop, those effects are not guaranteed.

If the CLI is not detected, use **高级 / 故障处理** (Advanced / troubleshooting) to select the official `codex.exe`. Machine-specific paths stay local. AI is optional: control types and slots can be assigned manually.

## Daily workflow

1. Select the PSD and enter a module/panel name. Keeping PSD sources outside `Assets` is recommended.
2. Click **解析 PSD** (Parse PSD); check the layer tree and preview.
3. Click **Codex 识别** (Codex recognition). Unity exports the task, starts the CLI and receives structured results. Advanced options retain manual file handoff.
4. Review results beside the PSD thumbnails. Check control type, part ownership and any proposed grouping/reparenting. Apply individual plans or select the desired plans together.
5. Click **应用所选并生成 Prefab** (Apply selected and generate prefab), or generate from the main window after applying changes.
6. Inspect the prefab in a Canvas. Saving successfully does not mean all interactions passed.
7. For a changed PSD, reuse the original tree/output location, inspect the change preview and update incrementally. Do not delete the prefab or its `.meta` just to refresh it.

Recognition tasks are local project data under `Library/PsdTool/CodexTasks`. They contain screenshots and layer metadata for AI processing; this is a cloud-assisted workflow, not fully offline. Obtain permission to process the artwork with the service. The official CLI manages authentication; no browser cookies or account credentials are bundled with the tool.

## Artist handoff by control

- **General:** use a root group per control where practical, avoid unnecessary wrappers and keep component parts separate. Save with Photoshop's **Maximize Compatibility** enabled. Preserve editable text and document default/selected/disabled states.
- **Button:** keep the button background, labels and icons together. Icon-only buttons and primary/secondary labels are supported. Its generated hit rectangle follows the union of normal visible artwork; hidden or pressed/disabled state images do not enlarge it.
- **Toggle:** group the background, checkmark and optional label. Make the selection graphic distinguishable from the background and other state candidates.
- **ScrollView:** provide a clear viewport/background, separate track and thumb, and grouped list items. Keep titles separate from the clipping background. Ordinary lists retain all PSD items, including those extending along the scroll direction.
- **Dropdown:** group the header background, caption, arrow and list. Place grouped options inside the list and structure each option as a Toggle. The parent plan can own the list and option bindings.
- **InputField:** provide a background and separate placeholder text. The builder can create the input-text node.
- **Slider / Progress:** keep track/fill separate; Slider also needs a handle. For radial or ambiguous Progress, select fill mode/direction, origin and initial amount in review, then validate/apply. Specify the fill slot if several images are plausible.

Artists do not need to build Unity's Viewport, Content, Sliding Area or component references. AI can propose some grouping/ownership repairs, but missing independent parts cannot be recovered from a flattened image.

## Review and incremental safeguards

Normal suggestions and completed items are collapsed; focus on items requiring review. A diagnosed control issue can be isolated as visual output while other valid controls are generated. Locate and resolve the reported interaction issues afterward.

Where a complete recovery proposal exists, **AI 覆盖并应用此项** (AI override and apply this item) explicitly permits the proposed marking changes. Source checks, ownership conflicts and hard locks still apply. A child already handled by a valid parent proposal is not applied twice. Empty recovery plans do not offer this action.

Preserve source mappings, import baselines and `.meta` files. Preview changes and use property locks for manual edits. Existing scripts, events and manual adjustments are protected according to those rules, not by a blanket promise that every edit is automatically preserved.

New virtual groups use English names; original artist layer names need not be renamed.

## Resources and scrolling

Use **Tools → PSD 工具 → 资源管理** (Resource management) to review reuse, nine-slice/import settings and references. Paths are configurable. New texture platform presets do not replace device testing, and existing manual settings are protected.

Scrollbar travel follows its track background. With only a thumb image, the tool uses the viewport length. Handle size follows the native view/content ratio: empty or short content fills the track; overflowing content produces a shorter handle. Runtime code/layout must update Content dimensions when items change.

## Acceptance and common issues

- Compare the generated layout, text and artwork with the PSD.
- Click the whole intended button/Toggle region; verify the checkmark and group behavior.
- Open, select, close and reopen a Dropdown; test its option template and scrolling.
- Check ScrollView clipping, direction, Content and scrollbar references.
- Focus an InputField and test input/placeholder behavior.
- After a revision, verify script/event references and deliberate manual edits.
- Missing text/glyphs: import TMP resources and configure a suitable licensed font.
- Recognition fails: check CLI installation, login, network and account allowance. Old/stale task results require a new task; do not alter signatures or mix projects.
- Ambiguous parts: use the thumbnail to locate the source, fix groups/slots or select the type manually. Repeated renaming is not the intended repair workflow.
- FX fails: check Photoshop configuration and the diagnostic log. Unsupported effects may require an art-side adjustment.

Other Unity versions/platforms and arbitrary PSDs are not guaranteed. Nested reusable-prefab substitution and business binding-code generation are not implemented. Read the [English README](../../../README.en.md) for the project overview, data handling and MIT license terms.
