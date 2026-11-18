#!/usr/bin/env python3
"""Validate built pages, local links/fragments, images, metadata and sitemap.

No network requests or third-party packages are required. Used locally and by CI.
"""

import argparse
import json
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit
from xml.etree import ElementTree


class Page(HTMLParser):
    def __init__(self, path):
        super().__init__(convert_charrefs=True)
        self.path = path
        self.ids = set()
        self.duplicates = []
        self.links = []
        self.images = []
        self.meta = {}
        self.canonical = []
        self.alternates = {}
        self.h1_count = 0
        self.lang = None
        self.title = ''
        self.in_title = False
        self.in_json = False
        self.json_text = ''
        self.structured_data = []

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'html':
            self.lang = attrs.get('lang')
        if attrs.get('id'):
            id_ = attrs['id']
            if id_ in self.ids:
                self.duplicates.append(id_)
            self.ids.add(id_)
        if tag == 'h1':
            self.h1_count += 1
        if tag == 'title':
            self.in_title = True
        if tag == 'meta':
            self.meta[attrs.get('name') or attrs.get('property')] = attrs.get('content', '')
        if tag == 'link':
            if attrs.get('rel') == 'canonical':
                self.canonical.append(attrs.get('href'))
            if attrs.get('rel') == 'alternate':
                self.alternates[attrs.get('hreflang')] = attrs.get('href')
        if tag == 'script' and attrs.get('type') == 'application/ld+json':
            self.in_json = True
            self.json_text = ''
        if tag == 'img':
            self.images.append(attrs)
        if attrs.get('href'):
            self.links.append(attrs['href'])
        if attrs.get('src'):
            self.links.append(attrs['src'])
        if attrs.get('srcset'):
            self.links.extend(item.strip().split()[0] for item in attrs['srcset'].split(','))

    def handle_endtag(self, tag):
        if tag == 'title':
            self.in_title = False
        if tag == 'script' and self.in_json:
            self.structured_data.append(json.loads(self.json_text))
            self.in_json = False

    def handle_data(self, data):
        if self.in_title:
            self.title += data
        if self.in_json:
            self.json_text += data


def check(output):
    output = output.resolve()
    build = json.loads((output / '.site-build.json').read_text(encoding='utf-8'))
    base = urlsplit(build['base_url'])
    prefix = base.path.rstrip('/') + '/'
    pages = {}
    errors = []
    checked_links = 0

    def require(condition, message):
        if not condition:
            errors.append(message)

    def target_of(href, source):
        parts = urlsplit(href)
        if parts.scheme and parts.scheme not in ('http', 'https'):
            return None, None
        if parts.netloc and parts.netloc != base.netloc:
            return None, None
        path = unquote(parts.path)
        if path.startswith('/'):
            if not path.startswith(prefix):
                errors.append(f'{source.relative_to(output)}: URL misses project prefix: {href}')
                return None, None
            target = output / path[len(prefix):]
        elif path:
            target = source.parent / path
        else:
            target = source
        target = target.resolve()
        if not target.is_relative_to(output):
            errors.append(f'{source.relative_to(output)}: link escapes output: {href}')
            return None, None
        if target.is_dir():
            target /= 'index.html'
        return target, unquote(parts.fragment)

    for path in sorted(output.rglob('*.html')):
        page = Page(path)
        try:
            page.feed(path.read_text(encoding='utf-8'))
        except (ValueError, json.JSONDecodeError) as error:
            errors.append(f'{path}: invalid HTML/JSON-LD: {error}')
            continue
        pages[path] = page
        label = str(path.relative_to(output))
        require(page.lang in ('zh-CN', 'en'), f'{label}: missing supported document language')
        require(page.h1_count == 1, f'{label}: expected exactly one h1')
        require(bool(page.title.strip()), f'{label}: missing title')
        require(bool(page.meta.get('description')), f'{label}: missing description')
        require(len(page.canonical) == 1, f'{label}: expected one canonical')
        require(not page.duplicates, f'{label}: duplicate IDs {page.duplicates}')
        for language in ['zh-CN', 'en', 'x-default']:
            require(language in page.alternates, f'{label}: missing {language} language alternate')
        for key in ['og:title', 'og:description', 'og:url', 'og:image', 'twitter:card']:
            require(bool(page.meta.get(key)), f'{label}: missing {key}')
        for image in page.images:
            require('alt' in image, f'{label}: image has no alt attribute')
            require(int(image.get('width', '0')) > 0 and int(image.get('height', '0')) > 0, f'{label}: image has no dimensions')
        require(bool(page.structured_data), f'{label}: missing JSON-LD')
        if path.name != '404.html':
            expected = build['base_url'].rstrip('/') + '/' + path.relative_to(output).as_posix().removesuffix('index.html')
            require(page.canonical == [expected], f'{label}: wrong canonical, expected {expected}')
            require(page.meta.get('og:url') == expected, f'{label}: wrong Open Graph URL')
            require('noindex' not in page.meta.get('robots', ''), f'{label}: public page blocks indexing')
        for data in page.structured_data:
            require(data.get('@context') == 'https://schema.org', f'{label}: invalid schema context')
        page.links.extend(page.canonical + list(page.alternates.values()) + [page.meta.get('og:image', '')])

    for path, page in pages.items():
        for href in page.links:
            if not href:
                continue
            target, fragment = target_of(href, path)
            if target is None:
                continue
            checked_links += 1
            require(target.is_file(), f'{path.relative_to(output)}: broken local link {href}')
            if fragment and target in pages:
                require(fragment in pages[target].ids, f'{path.relative_to(output)}: missing fragment #{fragment} in {target.relative_to(output)}')
        for language, href in page.alternates.items():
            target, _ = target_of(href, path)
            if target in pages and language != 'x-default':
                require(pages[target].lang == language, f'{path.relative_to(output)}: hreflang {language} points to wrong language')

    sitemap = ElementTree.parse(output / 'sitemap.xml')
    urls = [node.text for node in sitemap.findall('.//{http://www.sitemaps.org/schemas/sitemap/0.9}loc')]
    require(len(urls) == len(set(urls)), 'Sitemap contains duplicate URLs')
    require(set(urls) == set(build['pages']), 'Sitemap does not match public pages')
    require(len(pages) == len(urls) + 1, 'Expected public pages plus one 404 page')
    require((output / '.nojekyll').exists(), 'Missing .nojekyll')
    require(build['base_url'] + '/sitemap.xml' in (output / 'robots.txt').read_text(encoding='utf-8'), 'Wrong sitemap URL in robots.txt')
    for href in urls:
        target, _ = target_of(href, output / 'index.html')
        require(target in pages, f'Sitemap target is not an HTML page: {href}')

    if errors:
        raise SystemExit('\n'.join(errors))
    print(f'PASS: {len(pages)} HTML pages, {checked_links} local links/resources, metadata, JSON-LD, language alternates and sitemap.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', nargs='?', type=Path, default=Path('.artifacts/pages'))
    check(parser.parse_args().output)
