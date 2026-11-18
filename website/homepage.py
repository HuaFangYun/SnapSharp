"""Original SnapSharp landing-page composition and progressive demo markup."""


def render(site):
    t, link, href = site.t, site.link, site.href
    # The build injects the existing icon helper, keeping one icon vocabulary.
    icon = site.home_icon
    content = site.home_content
    h = content['HOME']

    def tag(symbol, text):
        return f'<span class="mini-tag">{icon(symbol)}{t(text)}</span>'

    def picture(name, alt, caption, eager=False):
        return site.zoom_image(name, alt, caption, eager)

    def section_head(number, label, title, description):
        return f'<div class="chapter-heading"><div class="chapter-index"><span>{number}</span>{t(label)}</div><h2>{t(title)}</h2><p>{t(description)}</p></div>'

    def window_title(text, right=''):
        return f'<div class="demo-title"><span class="demo-dots" aria-hidden="true"><i></i><i></i><i></i></span><span>{t(text)}</span><span class="demo-title-end">{right}</span></div>'

    body = f'''<section class="studio-hero" aria-labelledby="hero-title">
      <div class="container hero-layout">
        <div class="hero-copy">
          <div class="hero-kicker"><span class="live-dot"></span> YOUR SCREEN. YOUR DEVICE.</div>
          <h1 id="hero-title">{t(h['headline'])}</h1>
          <p class="hero-description">{t(h['intro'])}</p>
          <div class="actions">{site.button(label=t(('免费下载 Windows 版', 'Free download for Windows')))}{site.button('#local-ai', False, t(('探索本地 AI', 'Explore local AI')), 'shield')}</div>
          <p class="hero-compat">Windows 10 / 11 · x64 <span>／</span> Apache-2.0 {t(('开源', 'open source'))}</p>
          <span class="offline-stamp">{icon('shield')}{t(('本地 AI · 模型配置后可离线', 'LOCAL AI · OFFLINE AFTER MODEL SETUP'))}</span>
          <div class="hero-shortcut"><span>{icon('capture')}</span><div>{t(('灵感不等人。先截下来。', 'An idea worth keeping? Capture it.'))}<kbd>Ctrl + Shift + A</kbd></div></div>
        </div>
        <div class="hero-workbench" aria-label="{t(('本地 AI 工作流示意', 'Local AI workflow illustration'))}">
          <div class="workbench-top"><span><i class="live-dot"></i> {t(('本地工作台', 'LOCAL WORKSPACE'))}</span><span>01 — 05</span></div>
          <div class="hero-document">
            <div class="document-top"><span class="document-mark">S /</span><span>{t(('研究笔记', 'RESEARCH NOTES'))}</span><span>001</span></div>
            <div class="document-caption">{t(('从屏幕，到你的工作流', 'FROM YOUR SCREEN TO YOUR WORKFLOW'))}</div>
            <div class="document-heading">{t(('让图片里的信息<br>继续流动。', 'Let the information<br>in your images flow.'))}</div>
            <div class="scan-copy"><span>{t(('文字、数字和公式，不必重新输入。', 'Text, numbers, formulas. Skip retyping.'))}</span><span>{t(('在本地，继续编辑、整理与表达。', 'Edit, organize, and create. All locally.'))}</span><i class="scan-beam" aria-hidden="true"></i></div>
            <div class="document-bottom"><span>f(x) = x² + y²</span><span>{icon('shield')} ON DEVICE</span></div>
            <span class="crop-corner top-left" aria-hidden="true"></span><span class="crop-corner top-right" aria-hidden="true"></span><span class="crop-corner bottom-left" aria-hidden="true"></span><span class="crop-corner bottom-right" aria-hidden="true"></span>
          </div>
          <div class="hero-result result-ocr"><div>{icon('text')}<span>OCR</span><i></i></div><strong>{t(('识别，留在本机。', 'Recognize. On device.'))}</strong><p>PaddleOCR / ONNX</p><div class="result-bars" aria-hidden="true"><b></b><b></b><b></b></div></div>
          <div class="hero-result result-formula"><span>{t(('公式 → LaTeX', 'FORMULA → LaTeX'))}</span><strong>x² + y² = 1</strong><code>x^2 + y^2 = 1</code></div>
          <div class="hero-result result-qwen"><span class="qwen-symbol">Q</span><div><strong>Qwen</strong><span>{t(('本地翻译', 'Local translation'))}</span></div><span aria-hidden="true">EN ↔ 中</span></div>
          <div class="workbench-bottom"><span>{icon('shield')} {t(('本地模型 · 本地处理', 'LOCAL MODELS · LOCAL PROCESSING'))}</span><span>{t(('功能流程示意', 'Workflow illustration'))}</span></div>
        </div>
      </div>
      <div class="container capability-strip"><span>{t(('你的本地图像工具箱', 'YOUR LOCAL IMAGE TOOLKIT'))}</span><div>{tag('text', ('OCR 文字', 'Text OCR'))}{tag('table', ('表格 XLSX', 'Tables / XLSX'))}{tag('formula', ('公式 LaTeX', 'Formulas / LaTeX'))}{tag('text', ('Qwen 翻译', 'Qwen translation'))}{tag('cutout', ('ONNX 抠图', 'ONNX cutouts'))}</div></div>
    </section>'''

    body += '<section class="chapter ai-chapter" id="local-ai"><div class="container">'
    body += section_head('01', ('核心能力 / LOCAL INTELLIGENCE', 'CORE CAPABILITIES / LOCAL INTELLIGENCE'), ('AI 很近，<br>就在你的电脑里。', 'Intelligence, right here.<br>On your own device.'), ('文字、表格、公式、翻译、抠图。五条本地处理路径，接住截图之后的工作。', 'Text, tables, formulas, translation, and cutouts. Five local workflows for what comes after your screenshot.'))
    body += '<div class="ai-grid"><article class="recognition-card ai-card"><div class="card-heading"><span class="card-code">01 / RECOGNITION</span>' + tag('shield', ('本地推理', 'Local inference')) + '</div><h3>' + t(('图片里的信息，<br>变成手里的内容。', 'Information in an image.<br>Content you can work with.')) + '</h3><p class="card-description">' + t(('中英文 OCR、表格转 Excel、公式转 LaTeX。选好模式，识别、核对，再继续使用。', 'Chinese/English OCR, tables to Excel, and formulas to LaTeX. Choose a mode, recognize, review, and keep working.')) + '</p>'
    body += '<div class="recognition-gallery" data-gallery><div class="mode-tabs" data-tabs aria-label="' + t(('本地识别模式', 'Local recognition modes')) + '">'
    recognition = [
        ('ocr', ('文字 OCR', 'Text OCR'), ('原图中的文字，不必再敲一遍。', 'Text in your image, without retyping.'), ('本地 PaddleOCR · 核对识别区域与文字后复制或导出。', 'Local PaddleOCR. Compare regions and text, then copy or export.'), 'offline-ocr'),
        ('table', ('表格 → Excel', 'Table → Excel'), ('让图片表格，回到单元格。', 'Give that table its cells back.'), ('SLANet + OCR · 核对行列与数值，再导出 XLSX / CSV。', 'SLANet + OCR. Check structure and values, then export XLSX / CSV.'), 'table-to-excel'),
        ('formula', ('公式 → LaTeX', 'Formula → LaTeX'), ('一个公式，接着写下去。', 'A formula, ready for your next draft.'), ('本地 LaTeX-OCR · 复制结果，或导出 .tex 后继续排版。', 'Local LaTeX-OCR. Copy the output or export .tex for typesetting.'), 'image-to-latex'),
    ]
    for name, label, *_ in recognition:
        body += link('#recognition-' + name, t(label))
    body += '</div>'
    for name, label, title, description, guide in recognition:
        article = next(a for a in content['ARTICLES'] if a['slug'] == guide)
        body += f'<div class="recognition-panel" id="recognition-{name}" data-panel><div class="recognition-image">{picture(name, article["title"], article["caption"])}</div><div class="recognition-detail"><div><strong>{t(title)}</strong><p>{t(description)}</p></div>{link(href("guides/" + guide + "/"), t(("操作教程 ↗", "Read guide ↗")), "small-link")}</div></div>'
    body += '</div></article>'
    body += '<article class="translation-card ai-card"><div class="card-heading"><span class="card-code">02 / TRANSLATION</span><span class="model-label">QWEN</span></div><h3>' + t(('读懂外文，<br>也可以不联网。', 'Read beyond languages.<br>Keep it on your device.')) + '</h3><p class="card-description">' + t(('OCR 提取文字，交给本地 Qwen 翻译。模型配置完成后，文字无需发往外部服务。', 'Extract text with OCR and translate with local Qwen. Once configured, your text stays off external services.')) + '</p><div class="translation-visual"><div class="language-route"><span>English</span><span aria-hidden="true">↓</span><span>简体中文</span></div>'
    body += picture('translation', ('SnapSharp 已有的本地 Qwen 中英文翻译结果局部', 'Existing local Qwen translation result from SnapSharp'), ('来自项目已有翻译结果的局部，未重新生成译文。', 'Detail of an existing translation result; no new translation was generated.'))
    body += '</div><div class="model-foot"><span class="live-dot"></span> Qwen + llama.cpp <span>CPU / Vulkan</span></div></article>'
    body += '<article class="cutout-card ai-card"><div class="cutout-copy"><div class="card-code">03 / BACKGROUND REMOVAL</div><h3>' + t(('背景退场，<br>让主体登场。', 'Less background.<br>More subject.')) + '</h3><p>' + t(('用本地 ONNX 模型分离主体，检查边缘后导出透明 PNG / WebP。拖动分界线，对照原图与实际结果。', 'Separate the subject with local ONNX models, check the edges, and export transparent PNG / WebP. Drag to compare the original and actual result.')) + '</p><div class="model-chips"><span>BiRefNet</span><span>U²-Net</span><span>ONNX</span></div><p class="tiny-note">' + t(('BiRefNet General Lite · 实际 CPU 输出<br>保留原图比例，棋盘格表示透明区域。', 'BiRefNet General Lite · actual CPU output.<br>Original proportions; checkerboard indicates transparency.')) + '</p></div>'
    body += '<div class="comparison-wrap"><div class="comparison" style="--split:50%">' + site.image('cutout-after', ('实际抠图结果', 'Actual background removal result')) + '<div class="comparison-before">' + site.image('cutout-before', ('抠图前的原图', 'Original before background removal')) + '</div><span class="compare-label before">' + t(('原图', 'BEFORE')) + '</span><span class="compare-label after">' + t(('本地抠图', 'AFTER')) + '</span><span class="compare-divider" aria-hidden="true"><b>↔</b></span><input class="compare-range" type="range" min="0" max="100" value="50" aria-label="' + t(('拖动比较抠图前后', 'Drag to compare before and after background removal')) + '"></div><span class="compare-hint">↔ ' + t(('拖动看看，背景如何退场', 'Drag to reveal the difference')) + '</span></div></article></div>'
    body += '<details class="image-notes"><summary>' + t(('关于演示与配图', 'About the demonstrations and images')) + '</summary><p>' + t(('识别图片采用真实 CPU 模型输出，保留错误；翻译为项目已有结果。界面由软件组件渲染，部分来自 1.0.4，可能与当前版本不同。首屏与下面的交互画布为原创功能示意，不在浏览器运行 AI。效果随输入、模型和设备而异。', 'Recognition images retain actual CPU model output and errors. Translation comes from an existing result. Interfaces are application component renders, some from 1.0.4, and may differ from the current release. The hero and interactive canvas are original illustrations; no AI runs in the browser. Results vary with input, model, and device.')) + ' ' + link('https://github.com/leelongjiang/SnapSharp/blob/main/Screenshot/Blog/README.md', t(('完整配图来源 ↗', 'Full image notes ↗'))) + '</p></details></div></section>'

    body += '<section class="chapter capture-chapter" id="features"><div class="container">'
    body += section_head('02', ('日常工具 / SCREEN ESSENTIALS', 'EVERYDAY TOOLS / SCREEN ESSENTIALS'), ('好用，<br>从按下快捷键开始。', 'A good workflow<br>starts with a shortcut.'), ('截图、标注、贴图、长截图与录屏。日常操作放在手边，顺手完成。', 'Capture, annotate, pin, scroll, and record. Keep everyday screen tools within reach.'))
    body += '<div class="canvas-grid"><article class="annotation-demo"><div class="canvas-card-head"><span class="card-code">CAPTURE / ANNOTATE</span><h3>' + t(('把重点，直接画出来。', 'Make the important parts clear.')) + '</h3><p>' + t(('点一下工具，试试不同的表达方式。', 'Pick a tool. Give your feedback a shape.')) + '</p></div><div class="annotation-toolbar" role="group" aria-label="' + t(('标注示意工具', 'Illustrative annotation tools')) + '">'
    tools = [('rectangle', 'capture', ('框选', 'Frame')), ('arrow', 'pin', ('箭头', 'Arrow')), ('highlight', 'text', ('高亮', 'Highlight')), ('number', 'check', ('序号', 'Number')), ('blur', 'table', ('模糊', 'Blur'))]
    for index, (key, symbol, label) in enumerate(tools):
        body += f'<button type="button" data-tool="{key}" aria-pressed="{"true" if index == 0 else "false"}">{icon(symbol)}<span>{t(label)}</span></button>'
    body += '</div><div class="annotation-surface" data-active-tool="rectangle">' + window_title(('项目工作台', 'Project workspace'), 'SnapSharp') + '<div class="sample-dashboard"><div class="sample-greeting"><span>WORKSPACE / 2026</span><h4>' + t(('把想法，清楚地表达出来。', 'Good ideas deserve clarity.')) + '</h4></div><div class="sample-stats"><div><span>' + t(('本周完成', 'Completed')) + '</span><strong>12<span>↗ 24%</span></strong><div class="mini-bars"><i></i><i></i><i></i><i></i><i></i><i></i><i></i></div></div><div><span>' + t(('进行中的任务', 'In progress')) + '</span><strong>03</strong><div class="sample-progress"><i></i></div></div></div><div class="sample-row"><span class="sample-avatar">S</span><div><strong>' + t(('整理发布清单', 'Prepare release checklist')) + '</strong><span>' + t(('产品与设计 / 今日', 'Product & Design / Today')) + '</span></div><span class="sample-status">' + t(('进行中', 'In progress')) + '</span></div><div class="annotation-target">' + t(('下一步，发布你的想法。', 'Next up: share your idea.')) + '<span>↗</span></div><span class="annotation-overlay mark-rectangle"></span><svg class="annotation-overlay mark-arrow" viewBox="0 0 160 100" aria-hidden="true"><path d="M8 6Q40 94 140 78m-23-20 25 21-30 9" fill="none" stroke="currentColor" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"/></svg><span class="annotation-overlay mark-number">1</span></div></div><div class="canvas-caption"><span class="live-dot"></span>' + t(('可交互的标注示意', 'Interactive annotation illustration')) + '<span>' + t(('选择工具查看效果', 'Select a tool to preview')) + '</span></div></article>'
    body += '<article class="pin-demo"><div class="canvas-card-head"><span class="card-code">PIN / KEEP IN SIGHT</span><h3>' + t(('参考资料，<br>一直在你身边。', 'Your references,<br>always within sight.')) + '</h3><p>' + t(('图片、文字、颜色都能贴。拖动下面的卡片，整理你的小工作台。', 'Pin images, notes, and colors. Drag the cards below to arrange this little workspace.')) + '</p></div><div class="pin-playground" aria-label="' + t(('贴图场景示意，可拖动卡片', 'Illustrative pin workspace with draggable cards')) + '"><div class="pin-card pin-landscape" data-draggable tabindex="0" aria-label="' + t(('参考图示意，拖动或用方向键移动', 'Reference illustration; drag or use arrow keys')) + '"><div class="landscape-art" aria-hidden="true"><i></i><b></b><span></span></div><div>01 / MOODBOARD <span>↗</span></div></div><div class="pin-card pin-note" data-draggable tabindex="0" aria-label="' + t(('待办便签，拖动或用方向键移动', 'Note; drag or use arrow keys')) + '"><span class="pin-tack" aria-hidden="true"></span><small>NOTE TO SELF</small><strong>' + t(('先捕捉，<br>再创造。', 'Capture first.<br>Create next.')) + '</strong><p>Ctrl + Shift + V</p></div><div class="pin-card pin-color" data-draggable tabindex="0" aria-label="' + t(('颜色贴图，拖动或用方向键移动', 'Color pin; drag or use arrow keys')) + '"><div></div><span>#D8F58A</span><small>216 / 245 / 138</small></div></div><div class="canvas-caption">' + icon('pin') + t(('图片 · 文字 · 颜色 / 场景示意', 'Image · Text · Color / Illustration')) + '</div></article></div>'
    body += '<div class="utility-grid">'
    for symbol, title, description, formats in [
        ('capture', ('长内容，一张收好。', 'Long content. One image.'), ('滚动截图与拼接预览，连续内容集中保存。', 'Scrolling capture and stitching previews keep continuous content together.'), 'SCROLL / STITCH'),
        ('video', ('把操作，演示清楚。', 'Show how it works.'), ('录制 MP4，或制作简短 GIF，让步骤更直观。', 'Record MP4 or make a short GIF to explain each step.'), 'MP4 / GIF'),
        ('image', ('输出，按你的需要。', 'An output that fits.'), ('图片、透明背景、分页 PDF。保存前调整格式和尺寸。', 'Images, transparency, paginated PDF. Choose format and size before saving.'), 'PNG / WEBP / PDF'),
    ]:
        body += f'<article><span class="utility-icon">{icon(symbol)}</span><h3>{t(title)}</h3><p>{t(description)}</p><span class="utility-formats">{formats}</span></article>'
    body += '</div></div></section>'

    body += '<section class="privacy-chapter"><div class="container privacy-layout"><div>'
    body += section_head('03', ('隐私与离线 / YOUR DEVICE', 'PRIVACY & OFFLINE / YOUR DEVICE'), ('你的图片，<br>你的电脑，<br><em>你的掌控。</em>', 'Your images.<br>Your device.<br><em>Your control.</em>'), ('本地 AI 处理不上传图片或文字。把需要的模型准备好，离线也能继续工作。', 'Local AI processing does not upload images or text. Prepare your models, then keep working offline.'))
    body += '</div><div class="privacy-right"><img src="' + site.url('assets/privacy-orbit.svg') + '" width="680" height="340" loading="lazy" alt="' + t(('图片在电脑内完成识别的原创流程示意', 'Original illustration of an image processed locally on a computer')) + '"><div class="privacy-facts"><div><span>01</span><strong>' + t(('先下载，再离线', 'Download, then go offline')) + '</strong><p>' + t(('首次需联网下载模型与运行组件。配置完成后，本地功能可离线使用。', 'Initial model and runtime downloads need internet. Configured local features then work offline.')) + '</p></div><div><span>02</span><strong>' + t(('按需配置模型', 'Choose your models')) + '</strong><p>' + t(('无需另装 Python。不同模型的下载量、速度与硬件需求不同。', 'No separate Python install. Download size, speed, and hardware needs depend on the model.')) + '</p></div><div><span>03</span><strong>' + t(('在线服务，自主选择', 'Online services are optional')) + '</strong><p>' + t(('在线翻译、以图搜图等需主动启用或确认。截图历史保存在本地，未加密。', 'Online translation and image search require enabling or confirmation. Local screenshot history is not encrypted.')) + '</p></div></div></div></div></section>'
    body += '<section class="chapter guide-chapter" id="guides"><div class="container">' + section_head('04', ('工作流 / PUT IT TO WORK', 'WORKFLOWS / PUT IT TO WORK'), ('从一个问题，<br>到下一步。', 'From a question<br>to your next step.'), ('用具体的操作，把本地 AI 用起来。', 'Practical walkthroughs to put local AI to work.')) + site.guides() + '</div></section>'
    body += '<section class="chapter start-chapter"><div class="container start-layout"><div class="start-intro"><span class="chapter-index">READY WHEN YOU ARE</span><h2>' + t(('你的新工作流，<br>三步开始。', 'Your new workflow.<br>Three simple steps.')) + '</h2>' + link(href('start/'), t(('完整上手指南 ↗', 'Full setup guide ↗')), 'small-link') + '</div><ol class="steps">'
    body += ''.join(f'<li><h3>{t(title)}</h3><p>{t(description)}</p></li>' for title, description in h['start_steps']) + '</ol></div></section>'
    body += '<section class="chapter faq-chapter" id="faq"><div class="container faq-layout"><div><span class="chapter-index">GOOD TO KNOW</span><h2>' + t(('还有几个，<br>你可能想问的。', 'A few things<br>you might be wondering.')) + '</h2></div>' + site.faq() + '</div></section>' + site.download()
    return body
