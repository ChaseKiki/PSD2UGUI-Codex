# Preview.13 — Button hit areas, native feedback and non-destructive nine-slicing

## 中文

- 选中节点树中的 Button →「选择按钮热区来源…」，多选图片、文字或组；按正常视觉并集计算根节点热区。全不选恢复默认，不移动画面。重新解析保留选择。
- Button 统一 ColorTint，TargetGraphic 取正常可见、面积最大的 Image，悬停/按下可用于初步验收。移除缩放设置入口，不再生成缩放或 SpriteSwap。
- 已接管且未锁定的旧 Button 随再次生成迁移，保留事件与 FileID。工具基线中的旧缩放组件无引用时移除；存在事件/脚本引用或外部资产依赖时保留兼容并报告。兼容脚本仍随包保留，避免 Missing Script。
- 九宫只设置 Sprite Border / Image.Sliced，不再缩图或抽取重复像素。Android/iOS 压缩及图集配置保留；外部显式引用图片不修改。
- 升级后如要恢复旧九宫缩图的完整切图尺寸，重新解析并导出切图，再生成 Prefab。共享/公共和手改资源继续受保护，不强行覆盖。
- 热区来源全部失效时回退默认并警告；纯文字按钮没有 Image 变色反馈，点击能力保留。

## English

- Select multiple image/text/group nodes as Button hit-area sources; the normal visual union controls the root hit area without moving artwork. Empty selection restores the default; choices survive reparsing.
- Native ColorTint uses the largest normally visible Image. No new press-scale or SpriteSwap behaviours are generated.
- Managed, unlocked old Buttons migrate on regeneration while preserving events and identity. Referenced legacy scaling scripts remain for compatibility and are reported; unreferenced managed instances are removed.
- Nine-slicing only changes Sprite Border / Image.Sliced, preserving exported pixels and dimensions. Platform compression, atlas profiles and external-resource protections remain in place.
- Re-export PSD sprites to restore previously reduced images. Shared, common and manually edited assets are not forcibly overwritten.

Tested with Windows / Unity 2022.3.62f3c1. Preview release; review project-specific layouts and interactions after updating.