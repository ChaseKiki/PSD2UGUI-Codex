# PSD2UGUI-Codex

**Unity 内的 PSD → uGUI Prefab 工具：从分层设计稿开始，用 Codex 辅助识别、重组、绑定，再由程序审核和生成。**

支持普通中文图层名称，减少规则命名、重复切图、手动摆放和组件引用绑定。当前为 **Preview**，内置版本 `2026.10.30-preview.11`；复杂页面仍需要人工验收。

- [下载 Unity 安装包](Releases/preview.11/PsdTool-2026.10.30-preview.11.unitypackage)
- [完整使用说明](Assets/PsdTool/Documentation/使用说明.md)
- [更新记录](Assets/PsdTool/Documentation/发布说明.md)
- [MIT 许可证](LICENSE)

## 核心功能

- **PSD 还原**：解析分层图片、文字、位置和层级，生成可编辑的 Unity uGUI Prefab；默认保持 PSD 布局，不主动猜测适配锚点。
- **Codex 辅助识别**：结合整页、区域截图、带编号图层图和结构信息，判断控件类型、部件归属，并提出补组、换父级等建议。新建组采用英文命名。
- **复合控件 Builder**：Button、Toggle、ScrollView、Dropdown、InputField、Slider、Progress 等按独立规则构建，关联 Graphic、Viewport、Content、勾选图和滚动条等引用。
- **可审核的结果**：以 PSD 缩略图定位待处理项，支持单条应用、明确覆盖标记和人工修正。父控件接管的子项不重复处理；可定位的控件异常局部降级为视觉输出，并列入报告。
- **增量更新**：维护来源与节点映射，按属性归属、锁定和变更预览保护已有脚本、事件及人工修改。不是删除整份 Prefab 后重建。
- **资源管理**：可配置输出目录，支持图片去重/复用、九宫格检查、图集、Android/iOS 新纹理配置，以及资源引用检查。已有人工导入设置受保护。
- **布局细节**：按钮热区取正常底图、文字等视觉并集；ScrollRect 的 Handle 按可视区/内容尺寸计算，空列表或不足一屏时铺满轨道。Progress 可手工配置线性或径向填充。
- **图片效果**：可调用本机 Photoshop，在临时副本上烘焙图片图层 FX；原 PSD 不直接修改。文字继续使用 TMP，部分效果近似还原。

## 环境与前置要求

- 已验证：**Windows、Unity 2022.3.62f3c1、uGUI 1.0.0、TextMeshPro 3.0.7**。其他版本和操作系统尚未系统验证。
- **AI 可选**：需要安装官方 Codex CLI、完成其账号登录，并具有可用网络和额度。仅登录 ChatGPT 网页不等于 CLI 已登录。工具不附带账号、订阅或模型额度，不要求 Ollama、本地模型或单独配置 OpenAI API Key。
- **Photoshop 可选**：需要保留图片 FX 时，安装可用的 Photoshop 并配置可执行文件。Photoshop、Unity、Codex 和字体均不随本仓库提供。
- 正式输入是 **分层 PSD**；当前解析器面向 8 位 RGB PSD/PSB，公开验收以 PSD 为主。单张截图无法恢复缺失的独立图层和交互部件。
- 中文文字需自行提供有授权、覆盖所需汉字的 TMP 字体资源。

## 安装与操作

1. 在你的 Unity 工程中安装 uGUI、TextMeshPro，并导入 **TMP Essential Resources**。
2. 通过 **Assets → Import Package → Custom Package** 导入上方安装包；或将本仓库 `Assets/PsdTool` 文件夹及相应 `.meta` 复制到现有 Unity 工程。仓库本身不是完整 Unity 示例工程，也不是 UPM 包。
3. 打开 **Tools → PSD 工具 → 打开工具**。首次运行会创建本项目配置，设置设计分辨率、资源/Prefab 输出目录和字体映射；如需 FX，再配置本机 Photoshop。
4. 选择 PSD，点击 **解析 PSD**。
5. 点击 **Codex 识别**。按首次使用引导安装、登录 CLI，随后等待识别结果。日常流程由 Unity 启动 CLI 并接收返回，不需要把指令逐次粘贴到聊天窗口；高级选项保留手动任务模式。
6. 审核结果中的待处理项、缩略图和结构建议，应用需要的方案，再 **生成 Prefab**。不使用 AI 时，可手工选择控件类型与插槽后生成。
7. 在 Canvas 中验收视觉、点击热区、输入、勾选、下拉和滚动；改稿后先看变更预览，再增量生成并检查程序引用。

资源入口：**Tools → PSD 工具 → 资源管理**。不确定的区域不会保证自动绑定成功，生成成功也不代表每个控件交互已经验收。

## 美术需要交付什么

**不要求规则命名，中文普通名称即可；仍需提供合理分组和独立部件。**

- 尽量一个控件一个根组；背景、文字、图标、箭头、勾选图、轨道、滑块分别成层。AI 可以建议修复部分散图和错组，但不能保证任意层级都正确。
- Toggle 的底框、勾选图、标签放在同组；相似选中/禁用状态应提供用途说明。
- ScrollView 的可视区域、轨道、滑块可区分，每个列表项独立成组。
- Dropdown 将顶条背景、当前文字、箭头和展开列表放在同一根组；选项组放在列表内，并按 Toggle 组织。
- InputField 至少提供背景和独立提示文本；输入文字节点可由工具补齐。Slider 保留独立轨道、填充和滑块。
- 保留可编辑文字并提供字体信息；不要求将全部文字栅格化。
- 保存 PSD 时开启“最大兼容”，保留当前合成预览。美术不需要制作 Unity 的 Viewport、Content、Sliding Area 等辅助节点。

## 当前边界

- 仍需程序审核控件语义、图层归属及运行时适配；没有多稿泛化准确率承诺。
- 部分 Photoshop 混合、遮罩和文字效果无法完全还原。缺部件或多候选时需人工选择。
- 通用嵌套 Prefab 自动替换、业务 UI 绑定代码生成尚未接入。
- 增量保护依赖稳定来源、基线和属性锁；不要删除 `.meta`、手工改写稳定标识或跳过变更审核。生成前请做好版本控制。
- 运行时删除条目后，项目程序/布局仍需同步更新 Content 的实际尺寸，原生 ScrollRect 才会更新滑块比例。
- 已做布局、资源、控件引用、增量及 Play 交互回归；这不等于所有页面或 Android/iOS 真机均验收通过。

## 数据与账号

启用 AI 时，Unity 会导出界面截图、图层编号/名称、文本、几何与层级等识别数据，并交由官方 Codex CLI 处理；**不是完全离线识别**。请在获得相关设计稿授权、允许使用该 AI 服务的前提下使用。

账号登录由官方 CLI 管理。工具不打包凭据、不读取浏览器 Cookie，也不代售或共享订阅额度。项目本地配置、生成节点树、识别任务、缓存和业务素材不属于本仓库的交付内容。

## 仓库内容

- `Assets/PsdTool/Editor`：解析器、Builder、增量合并、审核和编辑器窗口。
- `Assets/PsdTool/Runtime`：生成 UI 使用的轻量运行时辅助组件。
- `Assets/PsdTool/Photoshop`：图片 FX 烘焙脚本。
- `Assets/PsdTool/CodexSkill`：随任务提供的识别 skill 和结果协议。
- `Assets/PsdTool/Documentation`：使用文档和变更记录。
- `Releases/preview.11`：已验证的 Unity 安装包及 SHA256。

本仓库不含业务工程、PSD/图片/字体、历史项目配置、生成 Prefab、缓存或带本机路径的测试夹具。维护和报告问题请见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 许可证

本项目使用 **MIT License**，允许免费使用、修改、分发、商用，以及用于闭源项目；分发软件或其重要部分时，**必须保留版权声明和完整许可声明**。软件按现状提供，无担保。详见 [LICENSE](LICENSE)。

第三方软件、服务及其商标不因此获得本项目的授权；依赖说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。本项目与 Unity、Adobe、OpenAI 无官方隶属关系。
