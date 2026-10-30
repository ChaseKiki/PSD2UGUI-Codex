# Result contract, version 1

Inputs are the task's `task.json`, `overview.png` and every listed region PNG. `parentRegionId` is the closest ancestor that is also an exported region; `-1` means none. Rectangles are `[x,y,width,height]` in PSD top-left coordinates. Input hashes are for Unity verification; copy the source signature, do not recalculate or modify it.

Output: UTF-8 JSON object in the same task directory, named **result.json**, without Markdown fences or surrounding prose:

```json
{
  "version": 1,
  "taskId": "COPY task.taskId EXACTLY",
  "signature": "COPY task.signature EXACTLY",
  "status": "completed",
  "regions": [
    {
      "rootId": 123,
      "type": "Toggle",
      "confidence": 0.92,
      "reason": "Checkbox with a visible check and adjacent label"
    }
  ]
}
```

The ID and classification above are examples, never expected answers. Replace the array with exactly one entry for each actual task region. Required fields:

- `version`: integer `1`.
- `taskId` and `signature`: exact strings from this task.
- `status`: literal `completed`, only after every supplied image was inspected.
- `regions`: complete, unique coverage of the task's root IDs. No missing, extra, duplicate or invented IDs.
- `type`: exactly `None`, `Button`, `Toggle`, `InputField`, `ScrollView`, `Dropdown`, `Slider` or `Progress`.
- `confidence`: finite number in `[0,1]`; no strings, NaN or Infinity.
- `reason`: nonempty visual evidence, at most 240 characters; ideally a short sentence.

Do not include executable paths, commands, scripts or credentials. Do not write a partial array as completed. Uncertain inspected regions may use `None`. If the task cannot be completed, report why instead of claiming completion.

## Optional structures (new tasks with `layers`)

Each confidently bindable primary widget needs a complete structures entry `{regionId, nodes: [...], groups: [...]}`; do not stop at classification. Omit only unsupported/uncertain controls or descendants covered by a parent proposal. `regionId` must be a classified region. One proposal must contain its entire compound widget, including list/item roots; nested regions handled by it need no separate proposal. Source layer IDs must come from `task.layers`; all supplied images are ordered overview, regions, then layerSheets. The numbers on layerSheets identify source image layers. Text layers are in the hierarchy table and overview/region images. Visually contained parts can have wrong source parents: compare rectangles to detect that and propose parent correction where clear.

Each node edit has all fields: `{layerId, setTemplate, setSlot, setParent, templateId, slot, parentId}`. False flags leave fields unchanged; use empty strings/-1 for unused values. Supported templates: Group, Button, Toggle, InputField, ScrollView, Dropdown, Slider, Progress. Roles: bg, label, placeholder, text, caption, arrow, template, item, checkmark, viewport, content, vbarbg, vbar, hbarbg, hbar, track, fill, handle. An unassigned decoration keeps its image/text and needs no role. A clear primary label may be bound while subtitle/decorative texts remain unchanged. An icon button needs no label. Do not relabel image/text layers as Group.

New groups have all fields `{groupId, parentId, templateId, slot, name, bounds}`. groupId must be a unique temporary integer below -1 (e.g. -1001); node parentId can reference it. parentId otherwise references an existing group or -1 for the canvas root. New groups must have existing source children, and may wrap an image as an InputField root while the image remains bg. Use `bounds: []` for ordinary groups; only viewport groups may give [x,y,width,height] within the PSD. Names are short display names only. For a single-widget region, at least one existing/new root must receive the classified template type. For a None layout container with clearly separated controls, supply their virtual/existing widget roots and complete source-part membership in ONE structure entry; leave the outer container generic. This split proposal is explicitly reviewed before application. Do not omit valid controls merely because the artist did not group them.

Avoid changes to manual/locked layers. Preserve visual placement, stacking and group opacity/clipping. Cross-group membership proposals within this PSD are allowed only when visually clear and remain subject to Unity's review/validation. Do not move parts belonging to another widget. Slider requires track/fill/handle; linear Progress requires track/fill. A radial Progress needs explicit supported radial handling; otherwise omit its structure and let Unity show unsupported status.

Unity rejects a wrong task ID/signature, stale PSD/tree, modified inputs, cross-project results, or malformed JSON. Per-region semantic problems are isolated and reported; valid regions remain reviewable. A successful import creates a review; it does not automatically modify a Prefab.
