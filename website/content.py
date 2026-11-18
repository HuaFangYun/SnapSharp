"""Editorial content. Each pair contains Simplified Chinese, then English.

Keep claims aligned with README.md, the user guide, and screenshot provenance.
Do not imply that illustrative UI renders are live desktop captures.
"""

HOME = {
    "title": ("SnapSharp — Windows 截图、本地 OCR 与图像工具", "SnapSharp — Windows Screenshots, Offline OCR & Image Tools"),
    "description": (
        "SnapSharp 是开源 Windows 截图工具，支持截图标注、桌面贴图、长截图与录屏。通过本地模型完成 OCR、表格转 Excel、公式转 LaTeX、抠图和翻译。",
        "An open-source Windows screenshot tool with annotations, desktop pins, scrolling capture and recording. Run OCR, table-to-Excel, image-to-LaTeX, background removal and translation locally.",
    ),
    "badge": ("开源软件 · Windows 桌面端", "OPEN SOURCE · MADE FOR WINDOWS"),
    "headline": ("截图之后，<br><em>还可以做更多。</em>", "Screenshots.<br><em>And what comes next.</em>"),
    "intro": ("截图、标注、贴图，再把图片变成文字、表格或公式。SnapSharp 将日常图像工作放在一起，让 AI 处理也可以留在本机。", "Capture, annotate, and pin. Turn images into text, tables, or formulas. SnapSharp brings everyday image tasks together, with AI processing that can stay on your device."),
    "hero_note": ("Windows 10 / 11 · x64 · Apache-2.0 开源", "Windows 10 / 11 · x64 · Apache-2.0 license"),
    "local_badge": ("你的图片，在你的电脑处理", "Your images. Processed on your device."),
    "local_badge_small": ("本地 OCR · 公式 · 表格 · 抠图 · 翻译", "Local OCR · formulas · tables · cutouts · translation"),
    "strip": [("截图 + 标注 + 贴图", "Capture + annotate + pin"), ("本地模型，按需下载", "Local models, downloaded as needed"), ("中英文界面", "English & Chinese interface")],
    "features_eyebrow": ("从截图开始", "START WITH A SCREENSHOT"),
    "features_title": ("常用的图像操作，顺手完成。", "Small tasks. A smoother workflow."),
    "features_intro": ("从记下一个想法，到整理一份资料。少一点窗口切换，多一点专注。", "From capturing an idea to preparing a document. Keep useful tools close, and keep your work moving."),
    "showcase_eyebrow": ("看看它能做什么", "A CLOSER LOOK"),
    "showcase_title": ("从图片，走到下一步。", "Your image is just the beginning."),
    "showcase_intro": ("看实际界面和示例结果，找到适合自己的工作方式。点击图片可放大。", "Explore the interface and example results. Click an image to take a closer look."),
    "privacy_eyebrow": ("本地 AI", "LOCAL AI"),
    "privacy_title": ("处理图片，<br>可以就在这台电脑。", "Keep the processing<br>close to home."),
    "privacy_intro": ("OCR、表格、公式、抠图与翻译都提供本地处理路径。先下载需要的模型，之后在本机完成这些 AI 操作。", "OCR, table and formula recognition, background removal, and translation all offer local processing. Download the models you need, then run these AI tasks on your device."),
    "privacy_checks": [("模型按需安装，无需另装 Python", "Install models as needed; no separate Python installation"), ("本地 AI 处理不上传图片或文字", "Local AI processing does not upload images or text"), ("在线翻译等可选服务需要主动启用和确认", "Optional online translation requires enabling and confirmation")],
    "path": [("导入图片", "Import image"), ("本地模型", "Local model"), ("得到结果", "Get results")],
    "path_badge": ("本地处理路径", "ON-DEVICE PROCESSING"),
    "path_note": ("首次下载模型需联网；硬件需求随模型而异。", "Initial model downloads need internet. Hardware requirements vary by model."),
    "guides_eyebrow": ("把功能用起来", "PUT IT TO WORK"),
    "guides_title": ("从一个具体问题开始。", "Start with something you need to do."),
    "guides_intro": ("三种常见场景，按步骤把截图变成可以继续使用的内容。", "Three everyday workflows for turning captured images into something you can work with."),
    "start_eyebrow": ("快速上手", "GETTING STARTED"),
    "start_title": ("第一次截图，<br>从这里开始。", "Your first capture<br>starts here."),
    "start_intro": ("普通截图与标注可直接使用。需要 AI 功能时，再到设置中下载对应模型。", "Capture and annotate right away. Download the relevant models in Settings when you want to use AI features."),
    "start_steps": [
        (("下载并解压", "Download and extract"), ("从 GitHub Releases 下载 Windows x64 发布包，保留整个程序目录，运行 <code>SnapSharp.exe</code>。", "Download the Windows x64 package from GitHub Releases. Keep the complete extracted folder and run <code>SnapSharp.exe</code>.")),
        (("找到托盘图标，开始截图", "Find the tray icon and capture"), ("软件启动后驻留任务栏托盘。按 <kbd>Ctrl + Shift + A</kbd> 框选区域，添加箭头、文字或马赛克。", "The app starts in the system tray. Press <kbd>Ctrl + Shift + A</kbd> to select an area, then add arrows, text, or pixelation.")),
        (("复制、保存，或继续处理", "Copy, save, or take it further"), ("将截图复制、导出或置顶贴图；需要识别时，打开托盘中的“识别粘贴板图片…”入口。", "Copy or export the capture, or pin it on your desktop. For recognition, open “Recognize clipboard image…” from the tray menu.")),
    ],
    "faq_title": ("你可能还想知道", "A few useful answers"),
    "faq_intro": ("开始之前，先了解运行方式与功能边界。", "What to know about setup, privacy, and everyday use."),
    "download_title": ("下一张截图，<br>试试多做一步。", "Make more of<br>your next screenshot."),
    "download_intro": ("开源 Windows 工具，下载解压即可开始。", "An open-source Windows tool. Download, extract, and get started."),
}

FEATURES = [
    ("capture", ("截图与标注", "Capture & annotate"), ("区域、窗口与全屏截图。用箭头、序号、文字、荧光笔和马赛克把重点说明白。", "Capture an area, a window, or your screen. Explain the details with arrows, numbers, text, highlights, and pixelation.")),
    ("text", ("本地文字与翻译", "Local OCR & translation"), ("提取中英文文字，接着阅读译文。提供本地 OCR 引擎和 Qwen 翻译模型。", "Extract Chinese and English text, then translate it. Use local OCR engines and a local Qwen translation model.")),
    ("table", ("表格与公式识别", "Tables & formulas"), ("表格图片转 CSV / XLSX，公式图片转 LaTeX。识别结果可检查后继续使用。", "Turn table images into CSV / XLSX and formula images into LaTeX. Review the results before taking them into your work.")),
    ("pin", ("桌面贴图", "Desktop pins"), ("把图片、文字或颜色放在手边，支持置顶、缩放、透明度调整与分组。", "Keep images, text, and color references close with pins, zoom, opacity controls, and groups.")),
    ("cutout", ("抠图与图像导出", "Cutouts & image export"), ("使用本地模型分离主体，导出透明 PNG / WebP。也支持 JPEG 和分页 PDF 等格式。", "Separate a subject with local models and export transparent PNG / WebP. Save other images as JPEG or paginated PDF.")),
    ("video", ("长截图与录屏", "Scroll & record"), ("拼接连续内容，保存长图；录制 MP4 操作演示，或制作简短 GIF 动图。", "Stitch continuous content into a long image. Record an MP4 walkthrough or create a short GIF.")),
]

SHOWCASES = [
    {"id": "annotate", "tab": ("截图标注", "Annotate"), "image": "annotation", "title": ("把重点，直接画出来。", "Make the important parts clear."), "description": ("截图之后接着标注。用箭头指出入口，用文字补充说明，用序号整理操作顺序。", "Annotate right after capturing. Point to a control, add an explanation, and number the steps."), "checks": [("箭头、矩形、文字、序号与模糊", "Arrows, rectangles, text, numbers, and blur"), ("支持撤销、重做与图层调整", "Undo, redo, and adjust layer order"), ("复制、保存，或直接置顶贴图", "Copy, save, or pin the result")], "alt": ("SnapSharp 标注编辑器在示例工作台上添加箭头、文字和序号", "SnapSharp annotation editor with arrows, text, and numbered markers on a sample workspace"), "caption": ("软件编辑器界面渲染，底图为自制示例。", "Rendered application editor with purpose-made sample content.")},
    {"id": "ocr", "tab": ("文字 OCR", "Text OCR"), "image": "ocr", "title": ("图片里的文字，拿出来用。", "Put the text in your image to work."), "description": ("本地 OCR 提取文字，结果与识别区域可以对照。复制、修正，再继续整理资料。", "Extract text with local OCR. Compare the result with the image regions, correct it, and continue working."), "checks": [("中英文识别", "Chinese and English recognition"), ("PaddleOCR / Windows OCR / Tesseract", "PaddleOCR / Windows OCR / Tesseract"), ("可接着使用本地翻译", "Continue with local translation")], "alt": ("SnapSharp 本地 PaddleOCR 的中文识别界面与实际输出", "SnapSharp local PaddleOCR interface and actual Chinese recognition output"), "caption": ("真实 CPU 模型输出，保留识别误差；界面为软件组件渲染。", "Actual CPU model output, including recognition errors, rendered using application components."), "guide": "offline-ocr"},
    {"id": "tables", "tab": ("表格与公式", "Tables & formulas"), "image": "table", "title": ("让表格，回到单元格。", "Give that table its cells back."), "description": ("识别表格结构与文字，检查、修正行列和内容，再导出 CSV 或 Excel 文件。公式也可以单独识别为 LaTeX。", "Recognize table structure and text, review rows and columns, and export CSV or Excel. Individual formulas can also become LaTeX."), "checks": [("本地 SLANet + OCR 表格识别", "Local SLANet + OCR table recognition"), ("支持行列、合并结构与文字修正", "Review rows, columns, merges, and text"), ("公式识别可导出 .tex", "Export recognized formulas as .tex")], "alt": ("SnapSharp 使用 SLANet 和 PaddleOCR 识别四行三列表格的实际结果", "SnapSharp actual SLANet and PaddleOCR result for a four-row, three-column table"), "caption": ("实际样例识别为 4 行 × 3 列，共 12 个单元格；效果随输入而异。", "Actual sample: 4 rows × 3 columns, 12 cells. Results vary with input."), "guide": "table-to-excel"},
    {"id": "cutouts", "tab": ("一键抠图", "Remove background"), "image": "cutout", "title": ("留下主体，换一种用法。", "Keep the subject. Find a new use."), "description": ("用本地模型去掉背景，对照原图检查边缘，再保存为支持透明度的图片。", "Remove a background with a local model, inspect the edges against the original, and save an image with transparency."), "checks": [("提供 U²-Net 与 BiRefNet 等模型", "Models include U²-Net and BiRefNet"), ("可选择 CPU / DirectML GPU", "Choose CPU / DirectML GPU"), ("透明 PNG / WebP 输出", "Transparent PNG / WebP output")], "alt": ("SnapSharp BiRefNet General Lite CPU 抠图的原图与实际透明背景结果对比", "Original image and actual transparent result from SnapSharp BiRefNet General Lite on CPU"), "caption": ("同一输入的实际 CPU 推理结果，棋盘格表示透明区域。", "Actual CPU inference on the same input; checkerboard indicates transparency.")},
    {"id": "pins", "tab": ("桌面贴图", "Desktop pins"), "image": "pins", "title": ("参考资料，就放在手边。", "Keep your references in sight."), "description": ("把图片、待办文字与颜色样本置顶到桌面，一边看资料，一边继续工作。", "Pin images, notes, and color samples on top of your desktop while you keep working."), "checks": [("图片、文字与颜色贴图", "Image, text, and color pins"), ("缩放、透明度与分组", "Zoom, opacity, and groups"), ("快捷键隐藏或显示全部贴图", "Show or hide all pins with a shortcut")], "alt": ("SnapSharp 图片、文字待办和颜色贴图的组合场景示意", "Composite illustration of SnapSharp image, text, and color pins"), "caption": ("真实贴图组件组合展示，为场景示意，非桌面操作实拍。", "Composite of real pin components illustrating a workflow; not a live desktop capture.")},
]

FAQ = [
    (("SnapSharp 支持哪些系统？", "Which systems does SnapSharp support?"), ("支持 Windows 10 2004 及以上版本和 Windows 11，x64 架构。当前不支持 macOS 或 Linux。", "Windows 10 version 2004 or later, and Windows 11, on x64. macOS and Linux are not currently supported.")),
    (("需要付费或安装 Python 吗？", "Do I need to pay or install Python?"), ("SnapSharp 以 Apache-2.0 许可证开源，公开发布包可免费下载。使用本地 AI 功能无需另装 Python；首次使用需下载对应模型。第三方组件与模型遵循各自许可。", "SnapSharp is open source under Apache-2.0, with free public release downloads. Local AI features do not need a separate Python installation. Download the relevant models first; third-party components and models have their own licenses.")),
    (("图片会上传到云端吗？", "Are my images uploaded to the cloud?"), ("本地 OCR、表格、公式、抠图与本地翻译在本机运行，不上传图片或文字。可选在线翻译和以图搜图等服务会涉及网络请求，需按提示主动启用或确认。", "Local OCR, table and formula recognition, background removal, and local translation run on your device without uploading images or text. Optional online translation and image search involve network requests and require enabling or confirmation.")),
    (("断网后还能使用吗？", "Can I use it offline?"), ("普通截图与标注可以离线使用。AI 功能在对应模型与运行组件下载、配置完成后可在本地使用；首次模型下载和可选在线服务需要联网。", "Capturing and annotating work offline. Local AI features work after the relevant models and runtime components are downloaded and configured. Initial downloads and optional online services need internet access.")),
    (("启动后没有窗口，怎么办？", "Why is there no window after launch?"), ("这是正常的托盘启动方式。请在任务栏通知区域找到 SnapSharp 图标，也可以直接按 Ctrl+Shift+A 截图。快捷键无响应时，到设置检查是否与其他软件冲突。", "The app starts in the system tray. Find the SnapSharp icon in the notification area, or press Ctrl+Shift+A to capture. If a shortcut does not respond, check Settings for conflicts with other apps.")),
    (("AI 识别结果需要检查吗？", "Should I check the AI results?"), ("需要。OCR 可能出现空格、标点和文字错误；表格需要核对结构和单元格；公式需要核对 LaTeX；抠图需要检查发丝、透明和复杂边缘。处理效果与输入、模型和硬件有关。", "Yes. OCR can make text, spacing, and punctuation errors. Check table structure and cell values, validate formula LaTeX, and inspect hair, transparency, and complex cutout edges. Results depend on the input, model, and device.")),
]

ARTICLES = [
    {
        "slug": "offline-ocr", "tag": ("文字识别", "TEXT RECOGNITION"), "icon": "text", "image": "ocr",
        "title": ("Windows 离线 OCR：不上传图片，提取中英文文字", "Offline OCR on Windows: extract text without uploading images"),
        "short_title": ("不上传图片，怎么识别文字？", "How do I extract text without uploading images?"),
        "description": ("使用 SnapSharp 本地 OCR 从截图和剪贴板图片提取中英文文字，了解模型配置、离线使用条件与结果校对方法。", "Use SnapSharp local OCR to extract Chinese and English text from screenshots and clipboard images. Set up models, work offline, and review recognition results."),
        "summary": ("需要从截图里复制文字，又希望处理留在本机？配置本地 OCR 后就可以开始。", "Need editable text from an image while keeping processing on your device? Start with local OCR."),
        "answer": ("SnapSharp 提供本地 OCR：配置好相应引擎和模型后，可以在 Windows 电脑上识别图片中的中英文文字，无需上传图片。第一次下载模型需要联网。", "SnapSharp offers local OCR on Windows. Once the engine and models are configured, it can recognize Chinese and English text on your device without uploading the image. Initial model downloads require internet."),
        "sections": [
            {"id": "prepare", "title": ("先准备什么", "Before you start"), "paragraphs": [("准备 Windows 10 2004+ 或 Windows 11 x64 电脑，以及完整解压的 SnapSharp 发布包。先启动软件，在任务栏托盘打开设置。", "Use Windows 10 2004+ or Windows 11 x64 and the complete extracted SnapSharp release. Launch the app and open Settings from its system tray icon."), ("到“设置 → OCR 与识别”，选择一键配置推荐 ONNX OCR，完成后保存设置。推荐中英文模型约 14.2 MB；这是 OCR 模型的下载量，不是整个软件的体积。", "In Settings → OCR and recognition, use the recommended ONNX OCR setup and save the settings. The recommended Chinese/English models are about 14.2 MB; this is the OCR model download, not the complete application size.")]},
            {"id": "steps", "title": ("从图片提取文字", "Extract text from an image"), "steps": [("复制一张图片到剪贴板；也可以先按 <kbd>Ctrl + Shift + A</kbd> 截取需要的内容。", "Copy an image to the clipboard, or capture the content with <kbd>Ctrl + Shift + A</kbd>."), ("在托盘菜单打开“识别粘贴板图片…”，选择文字 OCR 模式和本地引擎。截图选区、贴图和编辑器也提供识别入口。", "Open “Recognize clipboard image…” from the tray menu and choose text OCR with a local engine. Recognition is also available from capture selections, pins, and the editor."), ("运行识别，对照左侧图片区域与右侧文本。点击区域或原始识别行可以互相定位。", "Run recognition and compare the image regions on the left with the text on the right. Click a region or original recognition line to locate its counterpart."), ("检查内容后复制或导出文本。如需翻译，再单独配置本地翻译模型。", "Review the result, then copy or export the text. Configure the separate local translation model if you also need a translation.")]},
            {"id": "review", "title": ("识别后，重点检查这些地方", "What to check in the result"), "paragraphs": [("优先使用清晰、文字较大的原图。空格、标点、混合中英文、数字和复杂排版都可能出现误识别；不要把识别结果直接当作原文。", "Prefer a clear image with sufficiently large text. Spacing, punctuation, mixed languages, numbers, and complex layouts may cause errors. Treat the result as a draft to compare with the original."), ("本页示例保留了真实模型输出中的空格与文字错误，未用手工文本替换。处理速度和效果取决于输入、模型与硬件。", "The example shown here retains the real model's spacing and text errors, without replacing the output with hand-written text. Speed and results vary with the input, model, and hardware.")]},
            {"id": "offline", "title": ("离线与隐私边界", "Offline use and privacy"), "paragraphs": [("本地 OCR 不需要上传图片或文字，模型与运行组件准备好后可以离线识别。可选的在线翻译与以图搜图属于其他网络服务，需要按提示启用或确认。", "Local OCR does not upload images or text and works offline after models and runtime components are ready. Optional online translation and image search are separate network services that require enabling or confirmation."), ("截图历史是本地保存功能，不等于加密存储。处理敏感资料时，也应留意保存目录与历史设置。", "Screenshot history is stored locally and is not encrypted. When working with sensitive material, review your save location and history settings as well.")]},
        ],
        "caption": ("本地 PaddleOCR / ONNX CPU 的实际中文识别结果；软件组件渲染界面，示例包含识别误差。", "Actual Chinese output from local PaddleOCR / ONNX on CPU. Interface rendered using application components; recognition errors are retained."),
    },
    {
        "slug": "table-to-excel", "tag": ("表格识别", "TABLE RECOGNITION"), "icon": "table", "image": "table",
        "title": ("表格截图转 Excel：本地识别并导出 XLSX / CSV", "Table screenshot to Excel: recognize locally and export XLSX / CSV"),
        "short_title": ("表格截图，怎么转成 Excel？", "How do I turn a table screenshot into Excel?"),
        "description": ("使用 SnapSharp 的本地 SLANet 与 OCR 识别表格截图，检查行列、合并单元格和文字，再导出 Excel XLSX 或 CSV。", "Recognize table screenshots locally with SnapSharp SLANet and OCR. Review rows, columns, merged cells, and text, then export Excel XLSX or CSV."),
        "summary": ("把图片表格还原为可编辑的单元格，校对后带到 Excel 中继续使用。", "Turn a table image into editable cells, review them, and continue in Excel."),
        "answer": ("SnapSharp 可以使用本地 SLANet 表格结构识别与 OCR，把表格图片转换为可检查的行列和单元格内容，再导出 XLSX 或 CSV。识别完成后需要核对结构与数值。", "SnapSharp combines local SLANet table structure recognition with OCR to turn a table image into rows, columns, and cell values you can review, then export as XLSX or CSV. Verify the structure and numbers before use."),
        "sections": [
            {"id": "prepare", "title": ("先配置表格与 OCR 模型", "Set up table and OCR models"), "paragraphs": [("在“设置 → OCR 与识别”完成 OCR 配置，再到本地表格与公式区域下载表格模型并保存设置。首次下载需要联网，之后本地表格识别不上传图片。", "Configure OCR in Settings → OCR and recognition, then download the table model in the local tables and formulas section and save. Initial downloads need internet; local table recognition does not upload the image."), ("使用尽量清晰、边界完整的表格截图，避免裁掉行列标题。若图片中混有大量段落，先裁出表格区域。", "Use a clear screenshot containing the complete table, including row and column headings. If the image contains long paragraphs, crop it to the table first.")]},
            {"id": "steps", "title": ("从表格图片到 XLSX", "From a table image to XLSX"), "steps": [("复制表格图片，打开托盘中的“识别粘贴板图片…”；也可从截图选区进入识别。", "Copy the table image and open “Recognize clipboard image…” from the tray, or enter recognition from a capture selection."), ("切换到表格模式，使用已配置的本地表格模型运行识别。", "Switch to table mode and run recognition with the configured local table model."), ("检查结果中的行、列、跨行、跨列与单元格文字，必要时手动修正。", "Review rows, columns, row and column spans, and cell text. Correct the result where needed."), ("导出为 XLSX 或 CSV，打开文件继续编辑，并再次核对关键数字。", "Export XLSX or CSV, open the file for further editing, and check important numbers again.")]},
            {"id": "review", "title": ("数值与结构都要核对", "Verify both values and structure"), "paragraphs": [("先看行列是否对应，再检查表头、合并单元格、小数点、负号、千位分隔符和单位。识别出来的空白与 0 也需要区分。", "Check row and column alignment first, then headings, merged cells, decimal points, minus signs, thousands separators, and units. Distinguish empty cells from zeros."), ("截图只包含可见内容，无法恢复原 Excel 的公式、宏、隐藏工作表或原始工作簿逻辑。导出的是图片识别得到的数据与结构。", "A screenshot only contains visible content. It cannot restore the original Excel formulas, macros, hidden sheets, or workbook logic. The export contains data and structure recognized from the image.")]},
            {"id": "example", "title": ("这个示例说明了什么", "What the example demonstrates"), "paragraphs": [("本页使用软件固定版本的 SLANet 和 PaddleOCR，在 CPU 上识别一张自制表格，得到 4 行 × 3 列、共 12 个单元格。这是一个具体样例，不是所有表格的准确率承诺。", "The sample uses the application's pinned SLANet and PaddleOCR models on CPU to recognize a purpose-made table into 4 rows × 3 columns, or 12 cells. It demonstrates one input, rather than an accuracy claim for every table.")]},
        ],
        "caption": ("SLANet 与 PaddleOCR 的实际 CPU 识别结果：4 行 × 3 列，共 12 个单元格。", "Actual SLANet and PaddleOCR CPU result: 4 rows × 3 columns, 12 cells."),
    },
    {
        "slug": "image-to-latex", "tag": ("公式识别", "FORMULA RECOGNITION"), "icon": "formula", "image": "formula",
        "title": ("公式图片转 LaTeX：Windows 本地识别教程", "Image to LaTeX: recognize formulas locally on Windows"),
        "short_title": ("公式图片，怎么变成 LaTeX？", "How do I turn a formula image into LaTeX?"),
        "description": ("用 SnapSharp 本地 LaTeX-OCR 识别单个公式截图，复制 LaTeX 或导出 .tex，了解模型配置、公式裁剪和结果校对。", "Recognize an individual formula screenshot with SnapSharp local LaTeX-OCR. Copy LaTeX or export .tex, set up the models, and validate the result."),
        "summary": ("框选一个公式，把图片转换成可复制、可继续排版的 LaTeX。", "Select a formula and turn its image into LaTeX you can copy and typeset."),
        "answer": ("SnapSharp 提供基于本地 LaTeX-OCR 的公式识别。下载对应模型后，选择单个公式图片即可生成 LaTeX，复制结果或导出 .tex 文件；使用前需要核对符号与结构。", "SnapSharp offers formula recognition through local LaTeX-OCR. After downloading the models, select an image of an individual formula to generate LaTeX, copy it, or export a .tex file. Verify the symbols and structure before use."),
        "sections": [
            {"id": "prepare", "title": ("下载模型，准备清晰截图", "Download the models and prepare an image"), "paragraphs": [("在“设置 → OCR 与识别 → 本地表格与公式”配置公式识别模型，下载完成后保存设置。模型准备好后，公式识别可在本机完成。", "Configure the formula models in Settings → OCR and recognition → local tables and formulas, then save. Once the models are ready, recognition runs on your device."), ("建议一次框选单个公式，保留上下标、分式横线和完整括号，尽量排除正文、页码与相邻公式。", "Select one formula at a time. Keep superscripts, subscripts, fraction bars, and complete brackets, and exclude surrounding paragraphs, page numbers, and adjacent formulas.")]},
            {"id": "steps", "title": ("从公式截图到 LaTeX", "From a screenshot to LaTeX"), "steps": [("截图或复制需要处理的单个公式图片。", "Capture or copy an image of the individual formula."), ("打开识别窗口，切换到“公式（本地 LaTeX）”模式。", "Open the recognition window and select the local LaTeX formula mode."), ("运行识别，对照原图检查返回的 LaTeX。", "Run recognition and compare the returned LaTeX with the original image."), ("复制结果或导出 .tex，在自己的 LaTeX 编辑器中渲染并核对。", "Copy the result or export .tex, render it in your LaTeX editor, and verify it.")]},
            {"id": "review", "title": ("把结果渲染一次，再使用", "Render the result before using it"), "paragraphs": [("重点检查上下标、分式、根号、括号范围和相似字符。例如数字 1、字母 l 和大写 I 可能混淆。模型输出的空格或间距命令不一定与原稿一致。", "Check superscripts, subscripts, fractions, radicals, bracket scope, and similar-looking characters. The digit 1, lowercase l, and uppercase I can be confused. Spacing commands may differ from the original typesetting."), ("这项功能用于公式图片识别，不代表能够恢复整篇论文的版式或保证复杂手写公式识别。请以自己的样本测试效果。", "This feature recognizes formula images. It does not recover an entire paper's layout or guarantee recognition of complex handwriting. Evaluate it with your own examples.")]},
            {"id": "example", "title": ("示例：圆的方程", "Example: the equation of a circle"), "paragraphs": [("本页示例是在 CPU 上通过本地模型识别 <code>x² + y² = 1</code> 得到的实际结果。截图保留了模型输出的空格和 LaTeX 间距命令，未手工美化为标准答案。", "The example is actual local CPU recognition of <code>x² + y² = 1</code>. The screenshot retains the model's spacing and LaTeX spacing commands, rather than replacing them with a polished reference answer.")]},
        ],
        "caption": ("本地 LaTeX-OCR 实际 CPU 结果，界面由软件真实组件渲染。", "Actual local LaTeX-OCR CPU output, displayed using real application components."),
    },
]

START = {
    "title": ("SnapSharp 快速上手：下载、截图与本地 AI 配置", "Get started with SnapSharp: download, capture & set up local AI"),
    "description": ("下载 Windows x64 版 SnapSharp，了解托盘启动、截图快捷键、本地 OCR 与模型配置、系统要求和常见问题。", "Download SnapSharp for Windows x64. Learn tray startup, capture shortcuts, local OCR and model setup, system requirements, and common fixes."),
    "answer": ("从 GitHub Releases 下载并完整解压 Windows x64 发布包，运行 SnapSharp.exe。软件在托盘启动，按 Ctrl+Shift+A 即可截图；本地 AI 功能需先下载相应模型。", "Download and fully extract the Windows x64 package from GitHub Releases, then run SnapSharp.exe. The app starts in the system tray. Press Ctrl+Shift+A to capture; download the relevant models before using local AI features."),
}
