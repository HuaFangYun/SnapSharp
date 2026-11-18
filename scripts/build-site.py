#!/usr/bin/env python3
"""Build the bilingual GitHub Pages site using only the Python standard library."""

import argparse
import html
import json
import re
import runpy
import shutil
from pathlib import Path
from string import Template
from urllib.parse import urlsplit
from xml.sax.saxutils import escape as xml_escape

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "website"
CONTENT = runpy.run_path(str(SOURCE / "content.py"))
REPO = "https://github.com/leelongjiang/SnapSharp"
RELEASES = REPO + "/releases/latest"
GITEE = "https://gitee.com/lilongjiang/SnapSharp"

PATHS = {
    "download": '<path d="M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5"/>',
    "github": '<path d="M9 19c-4 1-4-2-6-2m12 5v-4a3.5 3.5 0 0 0-1-2.7c3.3-.4 6.7-1.6 6.7-7A5.4 5.4 0 0 0 19.2 4 5 5 0 0 0 19 1s-1.2-.4-4 1.5a13.8 13.8 0 0 0-7 0C5.2.6 4 1 4 1a5 5 0 0 0-.2 3A5.4 5.4 0 0 0 2.3 8.3c0 5.4 3.4 6.6 6.7 7A3.5 3.5 0 0 0 8 18v4"/>',
    "capture": '<path d="M8 3H3v5m13-5h5v5M3 16v5h5m13-5v5h-5"/><rect x="7" y="7" width="10" height="10" rx="1"/>',
    "text": '<path d="M4 5V3h16v2M12 3v18m-4 0h8M4 10h3m-3 4h3m10-4h3m-3 4h3"/>',
    "table": '<rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 9h18M3 15h18M9 3v18M15 3v18"/>',
    "pin": '<path d="m16 3 5 5-4 1-4 6-1 4-7-7 4-1 6-4 1-4ZM3 21l5-5"/>',
    "cutout": '<path d="m12 3 1.9 6.1L20 11l-6.1 1.9L12 19l-1.9-6.1L4 11l6.1-1.9L12 3ZM20 3v4m-2-2h4M4 17v4m-2-2h4"/>',
    "video": '<rect x="3" y="5" width="13" height="14" rx="2"/><path d="m16 10 5-3v10l-5-3"/>',
    "shield": '<path d="m12 3 8 3v6c0 4-4 7-8 9-4-2-8-5-8-9V6l8-3Z"/><path d="m8 12 3 3 5-6"/>',
    "monitor": '<rect x="3" y="3" width="18" height="14" rx="2"/><path d="M8 21h8m-4-4v4"/>',
    "image": '<rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8" cy="8" r="1"/><path d="m3 17 5-5 4 4 4-7 5 8"/>',
    "check": '<path d="m5 12 4 4L19 6"/>',
    "formula": '<path d="M19 3H8l6 9-6 9h11M3 7h2m-2 5h2m-2 5h2"/>',
}


def icon(name):
    return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' + PATHS[name] + '</svg>'


def esc(value):
    return html.escape(str(value), quote=True)


class Site:
    def __init__(self, base_url, output):
        self.base = base_url.rstrip("/")
        self.base_path = urlsplit(self.base).path.rstrip("/")
        self.output = output
        self.images = json.loads((SOURCE / "assets/images/manifest.json").read_text(encoding="utf-8"))
        self.template = Template((SOURCE / "templates/base.html").read_text(encoding="utf-8"))
        self.pages = []
        self.lang = 0

    def t(self, value):
        return value[self.lang] if isinstance(value, tuple) else value

    def url(self, path=""):
        return self.base_path + "/" + path.lstrip("/")

    def route(self, page="", lang=None):
        lang = self.lang if lang is None else lang
        return ("en/" if lang else "") + page

    def href(self, page=""):
        return self.url(self.route(page))

    def link(self, href, label, css="", extra=""):
        return f'<a href="{esc(href)}"' + (f' class="{css}"' if css else '') + (f' {extra}' if extra else '') + f'>{label}</a>'

    def button(self, href=RELEASES, primary=True, label=None, symbol="download"):
        label = label or self.t(("下载 Windows 版", "Download for Windows"))
        return self.link(href, icon(symbol) + esc(label), "button" + (" primary" if primary else ""))

    def image(self, name, alt, eager=False, sizes="(max-width: 780px) 90vw, 700px"):
        meta = self.images[name]
        srcset = ", ".join(f'{self.url("assets/images/" + name + "-" + str(w) + ".webp")} {w}w' for w in meta["widths"])
        src = self.url(f'assets/images/{name}-{meta["widths"][-1]}.webp')
        loading = 'loading="eager" fetchpriority="high"' if eager else 'loading="lazy"'
        return f'<img src="{src}" srcset="{srcset}" sizes="{esc(sizes)}" width="{meta["width"]}" height="{meta["height"]}" alt="{esc(self.t(alt))}" {loading} decoding="async">'

    def zoom_image(self, name, alt, caption, eager=False, sizes="(max-width: 780px) 90vw, 700px"):
        return self.link(self.url(f"assets/images/{name}-full.webp"), self.image(name, alt, eager, sizes), extra=f'data-zoom data-caption="{esc(self.t(caption))}"')

    def heading(self, eyebrow, title, intro=None):
        return f'<div class="section-heading"><div class="eyebrow">{esc(self.t(eyebrow))}</div><h2>{self.t(title)}</h2>' + (f'<p>{self.t(intro)}</p>' if intro else '') + '</div>'

    def checks(self, values):
        return '<ul class="check-list">' + ''.join(f'<li>{self.t(value)}</li>' for value in values) + '</ul>'

    def guides(self, exclude=None):
        cards = []
        for article in CONTENT["ARTICLES"]:
            if article["slug"] == exclude:
                continue
            body = f'<span class="tag">{esc(self.t(article["tag"]))}</span><h3>{esc(self.t(article["short_title"]))}</h3><p>{esc(self.t(article["summary"]))}</p><span class="card-link">{self.t(("查看教程", "Read the guide"))} <span aria-hidden="true">↗</span></span>'
            cards.append(self.link(self.href(f'guides/{article["slug"]}/'), body, "guide-card"))
        return '<div class="guide-grid">' + ''.join(cards) + '</div>'

    def download(self):
        h = CONTENT["HOME"]
        return '<section class="download-section"><div class="container download-card"><div><h2>' + self.t(h["download_title"]) + '</h2><p>' + self.t(h["download_intro"]) + '</p></div><div class="actions">' + self.button() + self.button(REPO, False, "GitHub", "github") + '</div></div></section>'

    def faq(self):
        return '<div class="faq-list">' + ''.join(f'<details><summary>{esc(self.t(q))}</summary><p>{esc(self.t(a))}</p></details>' for q, a in CONTENT["FAQ"]) + '</div>'

    def home(self):
        h = CONTENT["HOME"]
        shot = CONTENT["SHOWCASES"][0]
        body = f'<section class="hero"><div class="container"><div class="hero-grid"><div class="hero-copy"><div class="hero-badge"><span class="status-dot"></span>{self.t(h["badge"])}</div><h1>{self.t(h["headline"])}</h1><p>{self.t(h["intro"])}</p><div class="actions">{self.button()}{self.button(self.href("start/"), False, self.t(("快速上手", "Quick start")), "capture")}</div><p class="hero-note">{self.t(h["hero_note"])}</p></div><div class="hero-art"><div class="window-frame"><div class="window-title" aria-hidden="true"><i></i><i></i><i></i><span>SnapSharp / {self.t(("标注工作台", "Annotation workspace"))}</span></div>{self.zoom_image("annotation", shot["alt"], shot["caption"], True, "(max-width: 780px) 90vw, 620px")}</div><div class="floating-note"><span class="icon">{icon("shield")}</span><div><strong>{self.t(h["local_badge"])}</strong>{self.t(h["local_badge_small"])}</div></div></div></div><div class="hero-strip">'
        body += ''.join(f'<span>{icon(symbol)}{self.t(label)}</span>' for symbol, label in zip(["capture", "shield", "monitor"], h["strip"])) + '</div></div></section>'
        body += '<section class="section" id="features"><div class="container">' + self.heading(h["features_eyebrow"], h["features_title"], h["features_intro"]) + '<div class="feature-grid">'
        body += ''.join(f'<div class="feature-item"><span class="icon">{icon(symbol)}</span><h3>{self.t(title)}</h3><p>{self.t(description)}</p></div>' for symbol, title, description in CONTENT["FEATURES"]) + '</div></div></section>'
        body += '<section class="section showcase" id="screenshots"><div class="container" data-gallery><div class="showcase-top">' + self.heading(h["showcase_eyebrow"], h["showcase_title"], h["showcase_intro"]) + self.link(REPO + '/blob/main/Screenshot/Blog/README.md', self.t(("配图说明与来源 ↗", "Image notes & sources ↗")), "small-link") + '</div><div class="tabs" data-tabs aria-label="' + self.t(("功能截图", "Feature screenshots")) + '">'
        body += ''.join(self.link('#showcase-' + item['id'], esc(self.t(item['tab']))) for item in CONTENT['SHOWCASES']) + '</div>'
        for index, item in enumerate(CONTENT['SHOWCASES']):
            body += f'<div class="showcase-panel" id="showcase-{item["id"]}" data-panel><div class="showcase-image">{self.zoom_image(item["image"], item["alt"], item["caption"])}</div><div class="showcase-copy"><div class="number">0{index + 1} / SNAPSHARP</div><h3>{self.t(item["title"])}</h3><p>{self.t(item["description"])}</p>{self.checks(item["checks"])}'
            if item.get('guide'):
                body += self.link(self.href('guides/' + item['guide'] + '/'), self.t(("查看操作教程 →", "See the walkthrough →")), 'small-link')
            body += '</div></div>'
        body += '<p class="source-note">' + self.t(("配图包含真实软件组件渲染、实际模型结果与标明的组合场景；展示用例不代表对所有输入的效果承诺。部分界面来自 1.0.4，当前版本可能有所不同。", "Images include real application component renders, actual model output, and labeled composite scenes. Examples do not guarantee results for every input. Some interfaces are from 1.0.4 and may differ in the current version.")) + '</p></div></section>'
        body += '<section class="section" id="local-ai"><div class="container privacy-grid">' + '<div>' + self.heading(h['privacy_eyebrow'], h['privacy_title'], h['privacy_intro']) + self.checks(h['privacy_checks']) + '</div><div class="privacy-visual"><div class="local-path">'
        for index, (symbol, label) in enumerate(zip(['image', 'monitor', 'check'], h['path'])):
            if index:
                body += '<span class="path-line" aria-hidden="true"></span>'
            body += f'<div class="path-node"><span class="icon">{icon(symbol)}</span><span>{self.t(label)}</span></div>'
        body += f'</div><span class="local-badge">{icon("shield")}{self.t(h["path_badge"])}</span><p>{self.t(h["path_note"])}</p></div></div></section>'
        body += '<section class="section workflow-section" id="guides"><div class="container">' + self.heading(h['guides_eyebrow'], h['guides_title'], h['guides_intro']) + self.guides() + '</div></section>'
        body += '<section class="section"><div class="container getting-started"><div>' + self.heading(h['start_eyebrow'], h['start_title'], h['start_intro']) + '<div class="actions">' + self.link(self.href('start/'), self.t(("阅读完整上手指南 →", "Read the getting started guide →")), 'small-link') + '</div></div><ol class="steps">'
        body += ''.join(f'<li><h3>{self.t(title)}</h3><p>{self.t(description)}</p></li>' for title, description in h['start_steps']) + '</ol></div></section>'
        body += '<section class="section faq-section" id="faq"><div class="container faq-layout">' + self.heading(('常见问题', 'FAQ'), h['faq_title'], h['faq_intro']) + self.faq() + '</div></section>' + self.download()
        app = {"@type": "SoftwareApplication", "@id": self.base + "/#software", "name": "SnapSharp", "url": self.base + "/", "description": self.t(h['description']), "operatingSystem": "Windows 10 (2004+), Windows 11; x64", "applicationCategory": "MultimediaApplication", "license": REPO + "/blob/main/LICENSE", "downloadUrl": RELEASES, "image": self.base + '/assets/images/social-' + ('en' if self.lang else 'zh') + '.png', "softwareHelp": self.base + '/' + self.route('start/'), "isAccessibleForFree": True, "featureList": [self.t(x[1]) for x in CONTENT['FEATURES']]}
        faq = {"@type": "FAQPage", "mainEntity": [{"@type": "Question", "name": self.t(q), "acceptedAnswer": {"@type": "Answer", "text": self.t(a)}} for q, a in CONTENT['FAQ']]}
        self.write('', self.t(h['title']), self.t(h['description']), body, [app, faq])

    def article(self, article):
        title = self.t(article['title'])
        page = 'guides/' + article['slug'] + '/'
        body = self.article_header(title, self.t(article['description']), self.t(article['tag']))
        body += '<div class="container article-layout"><article class="article-body"><div class="answer"><p>' + self.t(article['answer']) + '</p></div>'
        for index, section in enumerate(article['sections']):
            body += f'<section aria-labelledby="{section["id"]}"><h2 id="{section["id"]}">{self.t(section["title"])}</h2>'
            body += ''.join('<p>' + self.t(p) + '</p>' for p in section.get('paragraphs', []))
            if section.get('steps'):
                body += '<ol>' + ''.join('<li>' + self.t(step) + '</li>' for step in section['steps']) + '</ol>'
            if index == 1:
                body += '<figure class="article-figure">' + self.zoom_image(article['image'], article['title'], article['caption']) + '<figcaption>' + self.t(article['caption']) + '</figcaption></figure>'
            body += '</section>'
        body += '<div class="article-end"><p>' + self.t(("更多操作、模型选择与故障处理，请查看", "For detailed operation, model choices, and troubleshooting, see the")) + ' ' + self.link(self.guide_url(), self.t(("完整使用指南", "full user guide"))) + self.t(('。', '.')) + '</p><div class="actions">' + self.button() + self.button(self.href('start/'), False, self.t(("首次配置指南", "Setup guide")), 'capture') + '</div></div></article>'
        body += self.toc([(section['id'], section['title']) for section in article['sections']]) + '</div>'
        body += '<section class="section workflow-section"><div class="container">' + self.heading(('继续探索', 'KEEP EXPLORING'), ('还有这些使用场景', 'More ways to use SnapSharp')) + self.guides(article['slug']) + '</div></section>'
        data = [{"@type": "TechArticle", "headline": title, "description": self.t(article['description']), "inLanguage": 'en' if self.lang else 'zh-CN', "image": self.base + '/assets/images/' + article['image'] + '-full.webp', "author": {"@type": "Organization", "name": "SnapSharp contributors", "url": REPO}, "mainEntityOfPage": self.base + '/' + self.route(page)}, self.breadcrumb_data(page, title)]
        self.write(page, title + ' | SnapSharp', self.t(article['description']), body, data, 'article')

    def guide_url(self):
        return REPO + '/blob/main/docs/' + ('UserGuide.en.md' if self.lang else '%E4%BD%BF%E7%94%A8%E6%8C%87%E5%8D%97.md')

    def article_header(self, title, description, tag):
        return '<div class="container"><nav class="breadcrumb" aria-label="' + self.t(('面包屑导航', 'Breadcrumb')) + '">' + self.link(self.href(), self.t(('首页', 'Home'))) + '<span aria-hidden="true">/</span><span>' + esc(tag) + '</span></nav><header class="article-header"><div class="eyebrow">' + esc(tag) + '</div><h1>' + esc(title) + '</h1><p>' + esc(description) + '</p><div class="article-meta"><span>Windows 10 / 11 · x64</span><span>' + self.t(('SnapSharp 官方指南', 'SnapSharp official guide')) + '</span></div></header></div>'

    def toc(self, sections):
        return '<aside class="article-aside"><h2>' + self.t(('本文内容', 'On this page')) + '</h2><nav aria-label="' + self.t(('文章目录', 'Table of contents')) + '">' + ''.join(self.link('#' + id_, esc(self.t(title))) for id_, title in sections) + '</nav>' + self.button(label=self.t(('下载 SnapSharp', 'Get SnapSharp'))) + '<p>' + self.t(('模型需按需下载。识别结果请核对原图。', 'Download the models you need. Check recognition results against the original.')) + '</p></aside>'

    def breadcrumb_data(self, page, title):
        return {"@type": "BreadcrumbList", "itemListElement": [{"@type": "ListItem", "position": 1, "name": self.t(('首页', 'Home')), "item": self.base + '/' + self.route()}, {"@type": "ListItem", "position": 2, "name": title, "item": self.base + '/' + self.route(page)}]}

    def start(self):
        s = CONTENT['START']
        title = self.t(s['title'])
        sections = [('download', ('下载与启动', 'Download and launch')), ('capture', ('截图快捷键', 'Capture shortcuts')), ('models', ('配置本地 AI', 'Set up local AI')), ('requirements', ('系统与数据', 'System and local data')), ('questions', ('常见问题', 'Common questions'))]
        body = self.article_header(title, self.t(s['description']), self.t(('快速上手', 'GETTING STARTED')))
        body += '<div class="container article-layout"><article class="article-body"><div class="answer"><p>' + self.t(s['answer']) + '</p></div><h2 id="download">' + self.t(sections[0][1]) + '</h2><ol>'
        body += ''.join(f'<li><strong>{self.t(title)}{self.t(("。", "."))}</strong> {self.t(description)}</li>' for title, description in CONTENT['HOME']['start_steps']) + '</ol><p>'
        body += self.t(('优先选择自包含发布包，无需另装 .NET Desktop Runtime。请保留解压后的完整目录，不要只移动 EXE。', 'Prefer a self-contained release, which needs no separate .NET Desktop Runtime. Keep the complete extracted directory rather than moving only the EXE.')) + '</p><div class="actions">' + self.button() + self.button(REPO + '/releases', False, self.t(('全部版本', 'All releases')), 'github') + '</div>'
        body += '<h2 id="capture">' + self.t(sections[1][1]) + '</h2>'
        body += self.table([('默认快捷键', 'Default shortcut'), ('功能', 'Action')], [('<kbd>Ctrl + Shift + A</kbd>', ('区域截图', 'Area capture')), ('<kbd>Ctrl + Shift + F</kbd>', ('全屏截图', 'Full-screen capture')), ('<kbd>Ctrl + Shift + W</kbd>', ('当前窗口截图', 'Current window capture')), ('<kbd>Ctrl + Shift + V</kbd>', ('剪贴板图片置顶贴图', 'Pin the clipboard image')), ('<kbd>Ctrl + Shift + H</kbd>', ('隐藏 / 显示全部贴图', 'Hide / show all pins'))])
        body += '<p>' + self.t(('快捷键可在设置中修改。若无响应，先检查与其他截图工具或系统快捷键的冲突。', 'Customize shortcuts in Settings. If a shortcut does not respond, check for conflicts with other capture tools or system shortcuts.')) + '</p><h2 id="models">' + self.t(sections[2][1]) + '</h2><p>' + self.t(('到设置中下载需要的模型，完成后点击“保存设置”。各功能按需配置，不必一次安装全部模型，也不需要另装 Python。首次下载需要联网。', 'Download the models you need in Settings, then click Save Settings. Configure features individually; you do not need all models or a separate Python installation. Initial downloads require internet.')) + '</p>'
        body += self.table([('功能', 'Feature'), ('设置入口与说明', 'Settings and notes')], [(('中英文 OCR', 'Chinese/English OCR'), ('OCR 与识别 → 推荐 ONNX OCR，模型约 14.2 MB', 'OCR and recognition → recommended ONNX OCR, about 14.2 MB for models')), (('表格与公式', 'Tables and formulas'), ('OCR 与识别 → 本地表格与公式', 'OCR and recognition → local tables and formulas')), (('一键抠图', 'Background removal'), ('一键抠图 → 下载模型，再选择默认模型', 'Background removal → download a model and choose the default')), (('本地翻译', 'Local translation'), ('翻译、API 与隐私 → 本地引擎，模型与运行库约 1.04 GiB', 'Translation, APIs, and privacy → local engine, about 1.04 GiB for model and runtime'))])
        body += '<p>' + self.t(('上表下载量来自当前项目指南，不包含软件发布包；模型选择不同，下载与硬件需求也会不同。在线翻译等可选服务需要另外启用或确认。', 'Sizes come from the current project guide and exclude the application download. Different model choices change download and hardware requirements. Optional online translation requires separate enabling or confirmation.')) + '</p><h2 id="requirements">' + self.t(sections[3][1]) + '</h2><ul>'
        requirements = [('Windows 10 2004+ 或 Windows 11，x64；当前不支持 macOS / Linux。', 'Windows 10 2004+ or Windows 11, x64; macOS / Linux are not supported.'), ('依赖框架发布包需要 .NET 10 Desktop Runtime；自包含包无需另装。', 'Framework-dependent releases need .NET 10 Desktop Runtime; self-contained releases do not.'), ('录屏可能需要 VC++ x64 运行库和 Windows 媒体组件，可先使用一键配置录屏环境。', 'Recording may require the VC++ x64 runtime and Windows media components. Use the recording environment setup first.'), ('设置、模型与历史默认保存在程序目录，可在“设置 → 常规 → 本地数据”修改。确认后下次启动会迁移相关数据。', 'Settings, models, and history default to the application folder. Change this in Settings → General → Local Data; migration takes place at the next launch after confirmation.'), ('截图历史未加密。敏感资料请同时检查保存目录、历史开关与文件访问权限。', 'Screenshot history is not encrypted. For sensitive material, review save locations, history settings, and file access permissions.')]
        body += ''.join('<li>' + self.t(p) + '</li>' for p in requirements) + '</ul><h2 id="questions">' + self.t(sections[4][1]) + '</h2>' + self.faq() + '<div class="article-end"><p>' + self.link(self.guide_url(), self.t(('查看完整使用指南 →', 'Read the full user guide →'))) + '</p></div></article>' + self.toc(sections) + '</div>'
        self.write('start/', title, self.t(s['description']), body, [self.breadcrumb_data('start/', title)])

    def table(self, headings, rows):
        return '<div class="table-wrap"><table><thead><tr>' + ''.join('<th scope="col">' + self.t(h) + '</th>' for h in headings) + '</tr></thead><tbody>' + ''.join('<tr>' + ''.join('<td>' + self.t(cell) + '</td>' for cell in row) + '</tr>' for row in rows) + '</tbody></table></div>'

    def write(self, page, title, description, body, structured, og_type='website', not_found=False):
        route = self.route(page)
        home = self.href()
        canonical = self.base + '/' + route
        nav = self.link(home + '#features', self.t(('功能', 'Features'))) + self.link(home + '#guides', self.t(('教程', 'Guides'))) + self.link(self.href('start/'), self.t(('快速上手', 'Get started'))) + self.link(REPO, 'GitHub') + self.link(self.url(self.route(page, 1 - self.lang)), '简体中文' if self.lang else 'English', 'language', 'lang="zh-CN" hreflang="zh-CN"' if self.lang else 'lang="en" hreflang="en"') + self.button(label=self.t(('下载', 'Download')))
        alternates = '\n  '.join(f'<link rel="alternate" hreflang="{language}" href="{self.base}/{self.route(page, language_index)}">' for language_index, language in [(0, 'zh-CN'), (1, 'en'), (0, 'x-default')])
        footer_links = self.link(REPO, 'GitHub') + self.link(GITEE, 'Gitee') + self.link(self.guide_url(), self.t(('使用指南', 'User guide'))) + self.link(REPO + '/blob/main/LICENSE', 'Apache-2.0') + self.link(REPO + '/blob/main/THIRD-PARTY-NOTICES.md', self.t(('第三方声明', 'Third-party notices')))
        data = {"@context": "https://schema.org", "@graph": structured}
        rendered = self.template.substitute(
            lang='en' if self.lang else 'zh-CN',
            title=esc(title), description=esc(description), canonical=esc(canonical),
            robots='<meta name="robots" content="noindex,follow">' if not_found else '',
            alternates=alternates, og_type=og_type,
            og_locale='en_US' if self.lang else 'zh_CN',
            social_image=self.base + '/assets/images/social-' + ('en' if self.lang else 'zh') + '.png',
            social_alt=self.t(('SnapSharp Windows 截图与本地图像工具，含实际编辑器界面', 'SnapSharp Windows screenshots and local image tools, with the actual editor interface')),
            asset_root=self.url('assets'),
            structured_data=json.dumps(data, ensure_ascii=False).replace('<', '\\u003c'),
            skip=self.t(('跳到正文', 'Skip to content')), home=home,
            home_label=self.t(('首页', 'home')), menu=self.t(('菜单', 'Menu')),
            navigation_label=self.t(('主导航', 'Main navigation')), navigation=nav, body=body,
            footer_tagline=self.t(('截图之后，还可以做更多。', 'Make more of your screenshots.')),
            footer_links=footer_links,
            footer_note=self.t(('Windows 桌面端 · 本地 AI · 中英文界面', 'Windows desktop · Local AI · English & Chinese')),
            image_dialog_label=self.t(('查看功能截图', 'View feature screenshot')),
            close_label=self.t(('关闭截图', 'Close screenshot')),
        )
        target = self.output / ('404.html' if not_found else route + 'index.html')
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(rendered, encoding='utf-8', newline='\n')
        if not not_found:
            self.pages.append(canonical)

    def build(self):
        self.output.mkdir(parents=True, exist_ok=True)
        shutil.copytree(SOURCE / 'assets', self.output / 'assets', dirs_exist_ok=True)
        # IndexNow uses a deliberately public text file to verify this site's
        # URL prefix. This is not an account credential or an OAuth token.
        indexnow_key = (SOURCE / 'indexnow-key.txt').read_text(encoding='utf-8').strip()
        if not re.fullmatch(r'[A-Za-z0-9-]{8,128}', indexnow_key):
            raise ValueError('Invalid public IndexNow key')
        (self.output / f'{indexnow_key}.txt').write_text(indexnow_key, encoding='utf-8')
        for self.lang in [0, 1]:
            self.home()
            self.start()
            for article in CONTENT['ARTICLES']:
                self.article(article)
        self.lang = 0
        body = '<section class="not-found container"><div class="eyebrow">SNAPSHARP</div><h1>404</h1><h2>这个页面还没被截到。</h2><p>页面可能已移动。回到首页，继续了解 SnapSharp。<br>This page could not be found. Head back to the homepage.</p><div class="actions" style="justify-content:center">' + self.button(self.url(), True, '返回首页', 'capture') + self.button(self.url('en/'), False, 'English homepage', 'capture') + '</div></section>'
        self.write('', '页面未找到 / Page not found | SnapSharp', '返回 SnapSharp 首页 / Return to SnapSharp.', body, [], not_found=True)
        # This is the actual crawler policy on a custom domain at its root.
        # A preview under a repository subpath cannot control host-root policy.
        (self.output / 'robots.txt').write_text(f'User-agent: *\nAllow: /\n\nSitemap: {self.base}/sitemap.xml\n', encoding='utf-8')
        sitemap = '<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n' + ''.join(f'  <url><loc>{xml_escape(url)}</loc></url>\n' for url in self.pages) + '</urlset>\n'
        (self.output / 'sitemap.xml').write_text(sitemap, encoding='utf-8')
        (self.output / '.nojekyll').touch()
        (self.output / '.site-build.json').write_text(json.dumps({'base_url': self.base, 'pages': self.pages}, indent=2), encoding='utf-8')
        print(f'Built {len(self.pages)} bilingual pages + 404 at {self.output}')
        print(f'Canonical base: {self.base}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-url', default='https://www.snapsharp.cn', help='Canonical Pages URL, including any deployment subpath')
    parser.add_argument('--output', type=Path, default=ROOT / '.artifacts/pages')
    args = parser.parse_args()
    parts = urlsplit(args.base_url)
    if parts.scheme not in ('http', 'https') or not parts.netloc or parts.query or parts.fragment or parts.username or parts.password:
        parser.error('--base-url must be an absolute HTTP(S) URL without credentials, query, or fragment')
    if any(part in ('.', '..') for part in parts.path.split('/')):
        parser.error('--base-url must not contain dot path segments')
    output = args.output.resolve()
    if output == ROOT or output in ROOT.parents or output == SOURCE or SOURCE in output.parents:
        parser.error('Build output must not overwrite the repository or website source')
    Site(args.base_url, output).build()


if __name__ == '__main__':
    main()
