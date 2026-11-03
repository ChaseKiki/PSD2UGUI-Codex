# Preview.14 — unified review, naming and generation confirmations

## 中文

- Codex 识别进度、结果审核与 Prefab 命名整合为一个工作台。
- 命名可逐条应用，类型/插槽/层级变化使受影响节点重新待确认；m_ 前缀已存在时对应按钮置灰。
- 提供完整节点清单、疑似遗漏筛查、批量 Codex 复核（每批最多 100 项）；预览点击定位，保留人工审核结果。
- Button 默认 None，生成前单独确认是否使用 ColorTint；支持一键全动画和全禁动画，无按钮时不弹窗。
- 九宫默认只设 Border；恢复人工批准的缩图，展示还原预览、像素误差、PNG 实测字节和 Android/iOS 纹理负载估算，不把它冒充最终包体收益。
- 原有增量更新、资源引用、GUID/FileID、事件与锁定保护继续生效。
- 此为通用开源包，不包含任何运营项目的私有组件、语言表或定制适配。

## English

- One workbench for Codex progress, recognition review and naming.
- Per-node naming approval with structural invalidation; the m_ shortcut is disabled when already prefixed.
- Full source inventory, possible omission leads and batch Codex reviews (up to 100 nodes), with preview-to-node navigation.
- Buttons default to None. Generation first confirms optional ColorTint with bulk enable/disable; no dialog without buttons.
- Explicitly reviewed nine-slice shrinking includes reconstruction previews, pixel errors, measured PNG bytes and platform texture-payload estimates. Final build size is not claimed.
- Incremental identity, references, events and locking protections remain.
- General-purpose package only; no private project adapters, dictionaries or artwork.
